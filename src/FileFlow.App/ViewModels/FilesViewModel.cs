using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using FileFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>Files page: search, filters, scan, details panel and batch operations.</summary>
public partial class FilesViewModel : ObservableObject, IRefreshable, ISelectionConsumer
{
    private const int SearchPageSize = 5000;
    private const int DebounceMilliseconds = 350;

    private readonly ILibraryService _library;
    private readonly IFileRepository _files;
    private readonly ITagRepository _tags;
    private readonly ITaskQueueService _queue;
    private readonly IImportExportService _importExport;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly RenamePlanner _renamePlanner;
    private readonly TransferPlanner _transferPlanner;
    private readonly ILogger<FilesViewModel> _logger;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _scanCts;
    private int _suppressSearch;
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    public FilesViewModel(
        ILibraryService library,
        IFileRepository files,
        ITagRepository tags,
        ITaskQueueService queue,
        IImportExportService importExport,
        ISettingsService settings,
        IDialogService dialogs,
        RenamePlanner renamePlanner,
        TransferPlanner transferPlanner,
        ILogger<FilesViewModel> logger)
    {
        _library = library;
        _files = files;
        _tags = tags;
        _queue = queue;
        _importExport = importExport;
        _settings = settings;
        _dialogs = dialogs;
        _renamePlanner = renamePlanner;
        _transferPlanner = transferPlanner;
        _logger = logger;

        ScanRecursive = settings.Current.ScanRecursive;
        ScanIncludeHidden = settings.Current.ScanIncludeHidden;
    }

    public ObservableCollection<FileRowViewModel> Results { get; } = new();

    /// <summary>Backed by DataGridSelectionBehavior (DataGrid.SelectedItems is not bindable).</summary>
    public IList SelectedRows { get; } = new ArrayList();

    public ObservableCollection<string> Extensions { get; } = new();

    public ObservableCollection<Tag> Tags { get; } = new();

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedExtension = "(all)";

    [ObservableProperty]
    private Tag? selectedTagFilter;

    [ObservableProperty]
    private string sizeMinText = string.Empty;

    [ObservableProperty]
    private string sizeMaxText = string.Empty;

    [ObservableProperty]
    private DateTime? modifiedFrom;

    [ObservableProperty]
    private DateTime? modifiedTo;

    [ObservableProperty]
    private bool onlyMissing;

    [ObservableProperty]
    private FileRowViewModel? selectedFile;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int selectedCount;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool scanRunning;

    [ObservableProperty]
    private double scanProgressPercent;

    [ObservableProperty]
    private string scanStatusText = string.Empty;

    [ObservableProperty]
    private string scanFolder = string.Empty;

    [ObservableProperty]
    private bool scanRecursive;

    [ObservableProperty]
    private bool scanIncludeHidden;

    public bool HasSelection => SelectedCount > 0;

    public bool HasSelectionOfTwoOrMore => SelectedCount >= 2;

    // ------------------------------------------------------------------ refresh

    public async Task RefreshAsync()
    {
        await LoadFilterOptionsAsync();
        await SearchNowAsync();
    }

    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            var extensions = await _files.GetExtensionsAsync();
            var tags = await _tags.GetAllAsync();

            var selectedExtensionValue = SelectedExtension;
            Extensions.Clear();
            Extensions.Add("(all)");
            foreach (var extension in extensions)
            {
                Extensions.Add(extension);
            }

            SelectedExtension = extensions.Contains(selectedExtensionValue, StringComparer.OrdinalIgnoreCase)
                ? selectedExtensionValue
                : "(all)";

            var selectedTagId = SelectedTagFilter?.Id;
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
            }

            SelectedTagFilter = tags.FirstOrDefault(t => t.Id == selectedTagId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load filter options");
        }
    }

    // ------------------------------------------------------------------ search

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _ = DebouncedSearchAsync();
    }

    private async Task DebouncedSearchAsync()
    {
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            if (!token.IsCancellationRequested)
            {
                await SearchNowAsync();
            }
        }
        catch (TaskCanceledException)
        {
            // superseded by a newer keystroke
        }
    }

    partial void OnSelectedExtensionChanged(string value) => _ = SearchNowAsync();

    partial void OnSelectedTagFilterChanged(Tag? value) => _ = SearchNowAsync();

    partial void OnOnlyMissingChanged(bool value) => _ = SearchNowAsync();

    partial void OnModifiedFromChanged(DateTime? value) => _ = SearchNowAsync();

    partial void OnModifiedToChanged(DateTime? value) => _ = SearchNowAsync();

    partial void OnSizeMinTextChanged(string value) => _ = SearchNowAsync();

    partial void OnSizeMaxTextChanged(string value) => _ = SearchNowAsync();

    private SearchFilter BuildFilter()
    {
        var filter = new SearchFilter
        {
            Keyword = SearchText.Trim(),
            Limit = SearchPageSize,
        };

        if (!string.Equals(SelectedExtension, "(all)", StringComparison.Ordinal))
        {
            filter.Extension = SelectedExtension;
        }

        if (SelectedTagFilter is not null)
        {
            filter.TagIds = [SelectedTagFilter.Id];
        }

        if (TryParseBytes(SizeMinText, out var minSize))
        {
            filter.MinSizeBytes = minSize;
        }

        if (TryParseBytes(SizeMaxText, out var maxSize))
        {
            filter.MaxSizeBytes = maxSize;
        }

        if (ModifiedFrom is not null)
        {
            filter.ModifiedFromUtc = DateTime.SpecifyKind(ModifiedFrom.Value, DateTimeKind.Local).ToUniversalTime();
        }

        if (ModifiedTo is not null)
        {
            // include the whole end day
            filter.ModifiedToUtc = DateTime.SpecifyKind(ModifiedTo.Value.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();
        }

        if (OnlyMissing)
        {
            filter.Missing = true;
        }

        return filter;
    }

    private static bool TryParseBytes(string text, out long bytes)
    {
        bytes = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var multiplier = 1L;
        var suffix = trimmed.ToUpperInvariant();
        if (suffix.EndsWith("KB")) { multiplier = 1024; trimmed = trimmed[..^2]; }
        else if (suffix.EndsWith("MB")) { multiplier = 1024 * 1024; trimmed = trimmed[..^2]; }
        else if (suffix.EndsWith("GB")) { multiplier = 1024L * 1024 * 1024; trimmed = trimmed[..^2]; }

        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value))
        {
            return false;
        }

        bytes = (long)(value * multiplier);
        return bytes >= 0;
    }

    [RelayCommand]
    private async Task SearchAsync() => await SearchNowAsync();

    private async Task SearchNowAsync()
    {
        // Serialize searches: a query requested while another one is running must run
        // afterwards with the latest filter instead of being silently dropped.
        await _searchGate.WaitAsync();
        try
        {
            if (_suppressSearch == 1)
            {
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            IsSearching = true;
            var filter = BuildFilter();
            var (items, total) = await _files.SearchAsync(filter, token);

            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(new FileRowViewModel(item));
            }

            TotalCount = total;
            SelectedFile = null;
            SelectedRows.Clear();
            OnSelectionChanged();
            ScanStatusText = total >= SearchPageSize
                ? $"Showing first {SearchPageSize:N0} of {total:N0} matching files."
                : $"{total:N0} file(s) found.";
        }
        catch (OperationCanceledException)
        {
            // a newer search superseded this one
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            ScanStatusText = "Search failed: " + ex.Message;
        }
        finally
        {
            IsSearching = false;
            _searchGate.Release();
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _suppressSearch = 1;
        SearchText = string.Empty;
        SelectedExtension = "(all)";
        SelectedTagFilter = null;
        SizeMinText = string.Empty;
        SizeMaxText = string.Empty;
        ModifiedFrom = null;
        ModifiedTo = null;
        OnlyMissing = false;
        _suppressSearch = 0;
        await SearchNowAsync();
    }

    [RelayCommand]
    private async Task RefreshFilesAsync()
    {
        // Verify the currently listed files against the disk, then re-run the search.
        if (Results.Count > 0)
        {
            var ids = Results.Select(r => r.File.Id).ToList();
            try
            {
                var changed = await _library.RefreshMissingAsync(ids);
                if (changed > 0)
                {
                    ScanStatusText = $"{changed} file(s) updated after existence check.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Existence check failed");
            }
        }

        await SearchNowAsync();
    }

    // ------------------------------------------------------------------ scanning

    [RelayCommand]
    private void BrowseScanFolder()
    {
        var folder = _dialogs.PickFolder(ScanFolder, "Choose a folder to scan");
        if (folder is not null)
        {
            ScanFolder = folder;
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var folder = ScanFolder.Trim();
        if (folder.Length == 0)
        {
            _dialogs.ShowError("Scan", "Choose a folder to scan first.");
            return;
        }

        if (!Directory.Exists(folder))
        {
            _dialogs.ShowError("Scan", $"The folder does not exist:\n{folder}");
            return;
        }

        _scanCts = new CancellationTokenSource();
        ScanRunning = true;
        ScanProgressPercent = 0;
        ScanStatusText = "Scanning...";

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanStatusText = $"Scanning... {p.FilesFound:N0} files · {p.DirectoriesVisited:N0} folders · {p.ErrorCount} errors";
        });

        try
        {
            var options = new ScanOptions(ScanRecursive, ScanIncludeHidden);
            var result = await _library.ScanDirectoryAsync(folder, options, progress, _scanCts.Token);
            ScanStatusText = $"Scan finished: {result.Files.Count:N0} files indexed, {result.Errors.Count} error(s), {result.Elapsed.TotalSeconds:0.0} s.";
            await LoadFilterOptionsAsync();
            await SearchNowAsync();
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "Scan cancelled.";
            await SearchNowAsync();
        }
        catch (FileFlowException ex)
        {
            ScanStatusText = "Scan failed.";
            _dialogs.ShowError("Scan failed", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed unexpectedly");
            ScanStatusText = "Scan failed.";
            _dialogs.ShowError("Scan failed", "Unexpected error: " + ex.Message);
        }
        finally
        {
            ScanRunning = false;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    // ------------------------------------------------------------------ selection helpers

    public void OnSelectionChanged()
    {
        SelectedCount = SelectedRows.Count;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSelectionOfTwoOrMore));
        SelectedFile = SelectedRows.Count > 0 ? (FileRowViewModel)SelectedRows[0]! : null;
    }

    private List<FileRecord> SelectedRecords() =>
        SelectedRows.Cast<FileRowViewModel>().Select(r => r.File).ToList();

    private string ProtectedWarning(List<FileRecord> records)
    {
        foreach (var record in records)
        {
            if (Core.Validation.ProtectedPathGuard.IsProtected(record.FullPath, Core.Validation.ProtectedPathGuard.DefaultProtectedRoots))
            {
                return $"The selection contains files inside a critical system directory ({record.FullPath}). "
                       + "Batch operations here can damage Windows. Make sure you know what you are doing.";
            }
        }

        return string.Empty;
    }

    private TaskItemRecord[] BuildItems(IEnumerable<PlanEntry> entries) =>
        entries.Select(e => new TaskItemRecord
        {
            SourcePath = e.SourcePath,
            TargetPath = e.TargetPath,
        }).ToArray();

    // ------------------------------------------------------------------ batch operations

    [RelayCommand]
    private async Task RenameAsync()
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var inputs = records.Select(r => new RenameInput(r.Id, r.FullPath)).ToList();
        var preview = new RenamePreviewViewModel(
            new RenameRules(),
            rules => _renamePlanner.Plan(inputs, rules))
        {
            Title = "Batch rename",
            Description = $"Review how {records.Count:N0} file(s) will be renamed. The preview updates as you change the rules.",
        };

        if (!_dialogs.ShowBatchPreview(preview) || preview.CurrentPlan is null)
        {
            return;
        }

        var plan = preview.CurrentPlan;
        if (plan.ValidCount == 0)
        {
            _dialogs.ShowError("Rename", "No file can be renamed with these rules.");
            return;
        }

        await EnqueueWithConfirmationAsync(
            TaskType.Rename,
            $"Rename {plan.ValidCount:N0} file(s)",
            BuildItems(plan.Entries.Where(e => e.IsValid)),
            null);
    }

    [RelayCommand]
    private async Task ChangeExtensionAsync()
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var newExtension = _dialogs.PromptText(
            "Change extension",
            "New extension for the selected files (e.g. \".pdf\", leave empty to remove the extension):",
            string.Empty,
            value =>
            {
                var trimmed = value.Trim();
                if (trimmed.Length == 0)
                {
                    return null;
                }

                return trimmed.StartsWith('.') ? null : "The extension must start with a dot, e.g. \".pdf\".";
            });
        if (newExtension is null)
        {
            return;
        }

        var inputs = records.Select(r => new RenameInput(r.Id, r.FullPath)).ToList();
        var plan = _renamePlanner.PlanExtensionChange(inputs, newExtension.Trim());
        if (plan.ValidCount == 0)
        {
            _dialogs.ShowError("Change extension", "No file can be updated. " + plan.Entries.FirstOrDefault()?.Error);
            return;
        }

        var preview = new BatchPreviewViewModel
        {
            Title = "Change extension",
            Description = $"Extension will be changed to '{newExtension.Trim()}' for {plan.ValidCount:N0} file(s).",
        };
        preview.ReplaceItems(plan.Entries.Select(e => new PreviewItemViewModel
        {
            From = e.CurrentName,
            To = e.IsValid ? e.NewName : string.Empty,
            Error = e.Error,
        }));

        if (!_dialogs.ShowBatchPreview(preview))
        {
            return;
        }

        await EnqueueWithConfirmationAsync(
            TaskType.ChangeExtension,
            $"Change extension to '{newExtension.Trim()}' ({plan.ValidCount:N0} file(s))",
            BuildItems(plan.Entries.Where(e => e.IsValid)),
            null);
    }

    [RelayCommand]
    private async Task MoveAsync() => await TransferAsync(isCopy: false);

    [RelayCommand]
    private async Task CopyAsync() => await TransferAsync(isCopy: true);

    private async Task TransferAsync(bool isCopy)
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var targetDirectory = _dialogs.PickFolder(null, isCopy ? "Copy selected files to..." : "Move selected files to...");
        if (targetDirectory is null)
        {
            return;
        }

        var inputs = records.Select(r => new TransferInput(r.Id, r.FullPath)).ToList();
        var plan = _transferPlanner.Plan(inputs, targetDirectory, isCopy);
        if (plan.ValidCount == 0)
        {
            var firstError = plan.Entries.FirstOrDefault()?.Error ?? "Nothing to do.";
            _dialogs.ShowError(isCopy ? "Copy" : "Move", firstError);
            return;
        }

        var verb = isCopy ? "Copy" : "Move";
        var preview = new BatchPreviewViewModel
        {
            Title = $"{verb} to \"{targetDirectory}\"",
            Description = $"{plan.ValidCount:N0} file(s) will be {(isCopy ? "copied" : "moved")}. Files with conflicts are skipped.",
            Warning = ProtectedWarning(records),
            ConfirmButtonText = isCopy ? "Copy" : "Move",
        };
        preview.ReplaceItems(plan.Entries.Select(e => new PreviewItemViewModel
        {
            From = e.SourcePath,
            To = e.IsValid ? e.TargetPath : string.Empty,
            Error = e.Error,
        }));

        if (!_dialogs.ShowBatchPreview(preview))
        {
            return;
        }

        await EnqueueWithConfirmationAsync(
            isCopy ? TaskType.Copy : TaskType.Move,
            $"{verb} {plan.ValidCount:N0} file(s) to {targetDirectory}",
            BuildItems(plan.Entries.Where(e => e.IsValid)),
            null);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var toRecycleBin = _settings.Current.DeleteToRecycleBin;
        var destination = toRecycleBin ? "Recycle Bin" : "deleted permanently";

        var preview = new BatchPreviewViewModel
        {
            Title = toRecycleBin ? "Move to Recycle Bin" : "Delete files",
            Description = $"{records.Count:N0} file(s) will be {destination}. Their library records are kept and marked as missing.",
            Warning = ProtectedWarning(records),
            ConfirmButtonText = toRecycleBin ? "Move to Recycle Bin" : "Delete permanently",
        };
        preview.ReplaceItems(records.Select(r => new PreviewItemViewModel
        {
            From = r.FullPath,
            To = destination,
        }));

        if (!_dialogs.ShowBatchPreview(preview))
        {
            return;
        }

        var items = records.Select(r => new TaskItemRecord { SourcePath = r.FullPath }).ToArray();
        await EnqueueWithConfirmationAsync(
            TaskType.Delete,
            $"Delete {records.Count:N0} file(s) ({destination})",
            items,
            null);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var tags = await _tags.GetAllAsync();
        var pick = _dialogs.PickTag(tags, allowCreate: true);
        if (pick is null)
        {
            return;
        }

        var items = records.Select(r => new TaskItemRecord { SourcePath = r.FullPath }).ToArray();
        var metadata = JsonSerializer.Serialize(new TaskMetadata { TagName = pick.TagName });
        await EnqueueWithConfirmationAsync(
            TaskType.AddTag,
            $"Add tag '{pick.TagName}' to {records.Count:N0} file(s)",
            items,
            metadata);
    }

    [RelayCommand]
    private async Task RemoveTagAsync()
    {
        var records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        var tags = await _tags.GetAllAsync();
        var pick = _dialogs.PickTag(tags, allowCreate: false);
        if (pick is null)
        {
            return;
        }

        var items = records.Select(r => new TaskItemRecord { SourcePath = r.FullPath }).ToArray();
        var metadata = JsonSerializer.Serialize(new TaskMetadata { TagName = pick.TagName });
        await EnqueueWithConfirmationAsync(
            TaskType.RemoveTag,
            $"Remove tag '{pick.TagName}' from {records.Count:N0} file(s)",
            items,
            metadata);
    }

    private async Task EnqueueWithConfirmationAsync(TaskType type, string description, TaskItemRecord[] items, string? metadata)
    {
        try
        {
            await _queue.EnqueueAsync(type, description, items, metadata);
            ScanStatusText = $"Task queued: {description}";
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError("Could not queue task", ex.Message);
        }
    }

    // ------------------------------------------------------------------ file actions

    [RelayCommand]
    private void OpenFile(FileRowViewModel? row)
    {
        if (row is null || row.IsMissing)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = row.File.FullPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open file {Path}", row.File.FullPath);
            _dialogs.ShowError("Open file", "Windows could not open this file: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenContainingFolder(FileRowViewModel? row)
    {
        var target = row ?? SelectedFile;
        if (target is null || target.IsMissing)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target.File.FullPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open folder for {Path}", target.File.FullPath);
        }
    }

    // ------------------------------------------------------------------ import / export

    [RelayCommand]
    private async Task ExportAsync()
    {
        var defaultDirectory = _settings.Current.ExportDirectory;
        if (string.IsNullOrEmpty(defaultDirectory))
        {
            defaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var fileName = $"fileflow-export-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        var target = _dialogs.PickSaveCsvFile(fileName, defaultDirectory);
        if (target is null)
        {
            return;
        }

        try
        {
            IsSearching = true;
            var filter = BuildFilter();
            var result = await _importExport.ExportAsync(target, filter);
            ScanStatusText = $"Exported {result.FileCount:N0} file record(s).";
            _dialogs.ShowInfo("Export finished", $"{result.FileCount:N0} file record(s) exported to:\n{result.FilePath}");
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError("Export failed", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed");
            _dialogs.ShowError("Export failed", "Unexpected error: " + ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var source = _dialogs.PickOpenCsvFile();
        if (source is null)
        {
            return;
        }

        try
        {
            IsSearching = true;
            var result = await _importExport.ImportAsync(source);
            var errorText = result.Errors.Count > 0
                ? $"\n\n{result.Errors.Count} row(s) were skipped:"
                  + string.Join(string.Empty, result.Errors.Take(10).Select(e => $"\n  row {e.Row}: {e.Reason}"))
                  + (result.Errors.Count > 10 ? $"\n  ... and {result.Errors.Count - 10} more" : string.Empty)
                : string.Empty;

            _dialogs.ShowInfo(
                "Import finished",
                $"Imported {result.Imported:N0} new file record(s).\n"
                + $"Updated {result.Updated:N0} existing record(s).\n"
                + $"Skipped {result.SkippedDuplicates} duplicate row(s)."
                + errorText);
            await LoadFilterOptionsAsync();
            await SearchNowAsync();
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError("Import failed", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed");
            _dialogs.ShowError("Import failed", "Unexpected error: " + ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }
}
