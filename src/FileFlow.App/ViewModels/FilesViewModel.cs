using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;
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
    private int _suppressFilterSearch;
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

        ExtensionOptions.Add(new FilterOption(string.Empty, Loc.Instance["Files_AllExtensions"]));
        SelectedExtensionOption = ExtensionOptions[0];
        TagOptions.Add(new TagFilterOption(null, Loc.Instance["Files_AllTags"]));
        SelectedTagOption = TagOptions[0];
        ScanStatusText = Loc.Instance["Files_StatusIdle"];
    }

    public ObservableCollection<FileRowViewModel> Results { get; } = new();

    /// <summary>Backed by DataGridSelectionBehavior (DataGrid.SelectedItems is not bindable).</summary>
    public IList SelectedRows { get; } = new ArrayList();

    public ObservableCollection<FilterOption> ExtensionOptions { get; } = new();

    public ObservableCollection<TagFilterOption> TagOptions { get; } = new();

    [ObservableProperty]
    private FilterOption? selectedExtensionOption;

    [ObservableProperty]
    private TagFilterOption? selectedTagOption;

    [ObservableProperty]
    private string searchText = string.Empty;

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

    /// <summary>Selection summary shown in the action bar.</summary>
    public string SelectionText => Loc.Instance.F("Files_SelectedCount", SelectedCount);

    // ------------------------------------------------------------------ refresh

    public async Task RefreshAsync()
    {
        await LoadFilterOptionsAsync();
        await SearchNowAsync();
    }

    /// <summary>Re-renders the generated text after a language change and re-runs the query.</summary>
    public async Task RefreshLocalizationAsync()
    {
        OnPropertyChanged(nameof(SelectionText));
        await RefreshAsync();
    }

    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            var extensions = await _files.GetExtensionsAsync();
            var tags = await _tags.GetAllAsync();

            var selectedExtensionValue = SelectedExtensionOption?.Value ?? string.Empty;
            _suppressFilterSearch++;

            ExtensionOptions.Clear();
            ExtensionOptions.Add(new FilterOption(string.Empty, Loc.Instance["Files_AllExtensions"]));
            foreach (var extension in extensions)
            {
                ExtensionOptions.Add(new FilterOption(extension, extension));
            }

            SelectedExtensionOption = ExtensionOptions.FirstOrDefault(
                o => string.Equals(o.Value, selectedExtensionValue, StringComparison.OrdinalIgnoreCase))
                ?? ExtensionOptions[0];

            var selectedTagId = SelectedTagOption?.TagId;
            TagOptions.Clear();
            TagOptions.Add(new TagFilterOption(null, Loc.Instance["Files_AllTags"]));
            foreach (var tag in tags)
            {
                TagOptions.Add(new TagFilterOption(tag.Id, tag.Name));
            }

            SelectedTagOption = TagOptions.FirstOrDefault(o => o.TagId == selectedTagId) ?? TagOptions[0];
            _suppressFilterSearch--;
        }
        catch (Exception ex)
        {
            _suppressFilterSearch = 0;
            _logger.LogError(ex, "Could not load the filter options");
        }
    }

    // ------------------------------------------------------------------ search

    partial void OnSearchTextChanged(string value) => QueueSearch();

    partial void OnSelectedExtensionOptionChanged(FilterOption? value) => QueueSearch();

    partial void OnSelectedTagOptionChanged(TagFilterOption? value) => QueueSearch();

    partial void OnOnlyMissingChanged(bool value) => QueueSearch();

    partial void OnModifiedFromChanged(DateTime? value) => QueueSearch();

    partial void OnModifiedToChanged(DateTime? value) => QueueSearch();

    partial void OnSizeMinTextChanged(string value) => QueueSearch();

    partial void OnSizeMaxTextChanged(string value) => QueueSearch();

    /// <summary>
    /// Every filter change goes through the same debounce: typing a size or flipping a
    /// checkbox used to fire one database query per keystroke.
    /// </summary>
    private void QueueSearch()
    {
        if (_suppressFilterSearch > 0)
        {
            return;
        }

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
            // superseded by a newer change
        }
    }

    private SearchFilter BuildFilter()
    {
        var filter = new SearchFilter
        {
            Keyword = SearchText.Trim(),
            Limit = SearchPageSize,
        };

        var extension = SelectedExtensionOption?.Value;
        if (!string.IsNullOrEmpty(extension))
        {
            filter.Extension = extension;
        }

        if (SelectedTagOption?.TagId is { } tagId)
        {
            filter.TagIds = [tagId];
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

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
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
                ? Loc.Instance.F("Files_StatusShowing", SearchPageSize, total)
                : Loc.Instance.F("Files_StatusFound", total);
        }
        catch (OperationCanceledException)
        {
            // a newer search superseded this one
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            ScanStatusText = Loc.Instance.F("Files_StatusSearchFailed", ex.Message);
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
        _suppressFilterSearch++;
        SearchText = string.Empty;
        SelectedExtensionOption = ExtensionOptions.FirstOrDefault();
        SelectedTagOption = TagOptions.FirstOrDefault();
        SizeMinText = string.Empty;
        SizeMaxText = string.Empty;
        ModifiedFrom = null;
        ModifiedTo = null;
        OnlyMissing = false;
        _suppressFilterSearch--;
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
                    ScanStatusText = Loc.Instance.F("Files_StatusChecked", changed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The existence check failed");
            }
        }

        await SearchNowAsync();
    }

    // ------------------------------------------------------------------ scanning

    [RelayCommand]
    private void BrowseScanFolder()
    {
        var folder = _dialogs.PickFolder(ScanFolder, Loc.Instance["Files_DlgPickScanFolder"]);
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
            _dialogs.ShowError(Loc.Instance["Files_DlgScanTitle"], Loc.Instance["Files_DlgChooseFolderFirst"]);
            return;
        }

        if (!Directory.Exists(folder))
        {
            _dialogs.ShowError(Loc.Instance["Files_DlgScanTitle"], Loc.Instance.F("Files_DlgFolderMissing", folder));
            return;
        }

        _scanCts = new CancellationTokenSource();
        ScanRunning = true;
        ScanProgressPercent = 0;
        ScanStatusText = Loc.Instance["Files_StatusScanStart"];

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanStatusText = Loc.Instance.F("Files_StatusScanning", p.FilesFound, p.DirectoriesVisited, p.ErrorCount);
        });

        try
        {
            var options = new ScanOptions(ScanRecursive, ScanIncludeHidden);
            var result = await _library.ScanDirectoryAsync(folder, options, progress, _scanCts.Token);
            ScanStatusText = Loc.Instance.F(
                "Files_StatusScanDone",
                result.Files.Count,
                result.Errors.Count,
                result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture));
            await LoadFilterOptionsAsync();
            await SearchNowAsync();
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = Loc.Instance["Files_StatusScanCancelled"];
            await SearchNowAsync();
        }
        catch (FileFlowException ex)
        {
            ScanStatusText = Loc.Instance["Files_StatusScanFailed"];
            _dialogs.ShowError(Loc.Instance["Files_DlgScanFailed"], ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The scan failed unexpectedly");
            ScanStatusText = Loc.Instance["Files_StatusScanFailed"];
            _dialogs.ShowError(Loc.Instance["Files_DlgScanFailed"], Loc.Instance.F("Files_DlgUnexpected", ex.Message));
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
        OnPropertyChanged(nameof(SelectionText));
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
                return Loc.Instance.F("Files_ProtectedWarning", record.FullPath);
            }
        }

        return string.Empty;
    }

    private static TaskItemRecord[] BuildItems(IEnumerable<PlanEntry> entries) =>
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
            Title = Loc.Instance["Files_RenameTitle"],
            Description = Loc.Instance.F("Files_RenameDesc", records.Count),
        };

        if (!_dialogs.ShowBatchPreview(preview) || preview.CurrentPlan is null)
        {
            return;
        }

        var plan = preview.CurrentPlan;
        if (plan.ValidCount == 0)
        {
            _dialogs.ShowError(Loc.Instance["Files_RenameTitle"], Loc.Instance["Files_DlgNoRename"]);
            return;
        }

        await EnqueueWithConfirmationAsync(
            TaskType.Rename,
            Loc.Instance.F("Files_DescRename", plan.ValidCount),
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
            Loc.Instance["Files_ExtTitle"],
            Loc.Instance["Files_ExtPrompt"],
            string.Empty,
            value =>
            {
                var trimmed = value.Trim();
                if (trimmed.Length == 0)
                {
                    return null;
                }

                return trimmed.StartsWith('.') ? null : Loc.Instance["Files_ExtInvalid"];
            });
        if (newExtension is null)
        {
            return;
        }

        var inputs = records.Select(r => new RenameInput(r.Id, r.FullPath)).ToList();
        var plan = _renamePlanner.PlanExtensionChange(inputs, newExtension.Trim());
        if (plan.ValidCount == 0)
        {
            _dialogs.ShowError(
                Loc.Instance["Files_ExtTitle"],
                Loc.Instance.F("Files_ExtNone", plan.Entries.FirstOrDefault()?.Error ?? string.Empty));
            return;
        }

        var preview = new BatchPreviewViewModel
        {
            Title = Loc.Instance["Files_ExtTitle"],
            Description = Loc.Instance.F("Files_ExtDesc", newExtension.Trim(), plan.ValidCount),
            ConfirmButtonText = Loc.Instance["Files_ExtTitle"],
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
            Loc.Instance.F("Files_DescChangeExt", plan.ValidCount, newExtension.Trim()),
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

        var targetDirectory = _dialogs.PickFolder(
            null,
            Loc.Instance[isCopy ? "Files_PickCopyTarget" : "Files_PickMoveTarget"]);
        if (targetDirectory is null)
        {
            return;
        }

        var inputs = records.Select(r => new TransferInput(r.Id, r.FullPath)).ToList();
        var plan = _transferPlanner.Plan(inputs, targetDirectory, isCopy);
        if (plan.ValidCount == 0)
        {
            var firstError = plan.Entries.FirstOrDefault()?.Error ?? Loc.Instance["Files_NothingToDo"];
            _dialogs.ShowError(Loc.Instance[isCopy ? "Files_CopyTitle" : "Files_MoveTitle"], firstError);
            return;
        }

        var verb = Loc.Instance[isCopy ? "Files_CopyTitle" : "Files_MoveTitle"];
        var preview = new BatchPreviewViewModel
        {
            Title = Loc.Instance.F("Files_TransferTitle", verb, targetDirectory),
            Description = Loc.Instance.F(
                "Files_TransferDesc",
                plan.ValidCount,
                Loc.Instance[isCopy ? "Files_Copied" : "Files_Moved"]),
            Warning = ProtectedWarning(records),
            ConfirmButtonText = verb,
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
            Loc.Instance.F("Files_DescTransfer", verb, plan.ValidCount, targetDirectory),
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
        var destination = Loc.Instance[toRecycleBin ? "Files_RecycleBin" : "Files_DeletedPermanently"];

        var preview = new BatchPreviewViewModel
        {
            Title = Loc.Instance[toRecycleBin ? "Files_DeleteRecycleTitle" : "Files_DeleteTitle"],
            Description = Loc.Instance.F("Files_DeleteDesc", records.Count, destination),
            Warning = ProtectedWarning(records),
            ConfirmButtonText = Loc.Instance[toRecycleBin ? "Files_DeleteRecycleTitle" : "Files_DeleteConfirm"],
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
            Loc.Instance.F("Files_DescDelete", records.Count, destination),
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
            Loc.Instance.F("Files_DescAddTag", pick.TagName, records.Count),
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
            Loc.Instance.F("Files_DescRemoveTag", pick.TagName, records.Count),
            items,
            metadata);
    }

    private async Task EnqueueWithConfirmationAsync(TaskType type, string description, TaskItemRecord[] items, string? metadata)
    {
        try
        {
            await _queue.EnqueueAsync(type, description, items, metadata);
            ScanStatusText = Loc.Instance.F("Files_StatusQueued", description);
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Files_DlgQueueFailed"], ex.Message);
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
            _logger.LogWarning(ex, "Could not open the file {Path}", row.File.FullPath);
            _dialogs.ShowError(Loc.Instance["Files_DlgOpenFailed"], Loc.Instance.F("Files_CannotOpen", ex.Message));
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
            _logger.LogWarning(ex, "Could not open the containing folder of {Path}", target.File.FullPath);
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
            ScanStatusText = Loc.Instance.F("Files_ExportDone", result.FileCount, result.FilePath);
            _dialogs.ShowInfo(
                Loc.Instance["Files_ExportTitle"],
                Loc.Instance.F("Files_ExportDone", result.FileCount, result.FilePath));
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Files_ExportFailed"], ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The export failed");
            _dialogs.ShowError(Loc.Instance["Files_ExportFailed"], Loc.Instance.F("Files_DlgUnexpected", ex.Message));
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
                ? Loc.Instance.F("Files_ImportSkipped", result.Errors.Count)
                  + string.Join(string.Empty, result.Errors.Take(10).Select(e => $"\n  {e.Row}: {e.Reason}"))
                  + (result.Errors.Count > 10 ? Loc.Instance.F("Files_ImportMore", result.Errors.Count - 10) : string.Empty)
                : string.Empty;

            _dialogs.ShowInfo(
                Loc.Instance["Files_ImportDone"],
                Loc.Instance.F("Files_ImportSummary", result.Imported, result.Updated, result.SkippedDuplicates) + errorText);
            await LoadFilterOptionsAsync();
            await SearchNowAsync();
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Files_ImportFailed"], ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The import failed");
            _dialogs.ShowError(Loc.Instance["Files_ImportFailed"], Loc.Instance.F("Files_DlgUnexpected", ex.Message));
        }
        finally
        {
            IsSearching = false;
        }
    }
}
