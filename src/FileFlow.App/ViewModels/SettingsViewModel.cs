using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Services;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Validation;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>Settings page: edits a working copy and saves through ISettingsService.</summary>
public partial class SettingsViewModel : ObservableObject, IRefreshable
{
    private static readonly IReadOnlyList<string> LogLevels = ["Trace", "Debug", "Information", "Warning", "Error"];

    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        ISettingsService settings,
        IDialogService dialogs,
        IAppPaths paths,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        LoadFromCurrent();
    }

    public ObservableCollection<string> LogLevelOptions { get; } = new(LogLevels);

    public string DataDirectoryText => _paths.DataDirectory;

    public string DatabasePathText => _paths.DatabasePath;

    public string LogsDirectoryText => _paths.LogsDirectory;

    [ObservableProperty]
    private bool scanRecursive;

    [ObservableProperty]
    private bool scanIncludeHidden;

    [ObservableProperty]
    private string exportDirectory = string.Empty;

    [ObservableProperty]
    private double maxConcurrentOperations;

    [ObservableProperty]
    private bool confirmDestructiveOperations;

    [ObservableProperty]
    private bool deleteToRecycleBin;

    [ObservableProperty]
    private string logLevel = "Information";

    public int ConcurrentOperationsValue => (int)Math.Round(MaxConcurrentOperations);

    public Task RefreshAsync()
    {
        LoadFromCurrent();
        OnPropertyChanged(nameof(DataDirectoryText));
        return Task.CompletedTask;
    }

    private void LoadFromCurrent()
    {
        var current = _settings.Current;
        ScanRecursive = current.ScanRecursive;
        ScanIncludeHidden = current.ScanIncludeHidden;
        ExportDirectory = current.ExportDirectory;
        MaxConcurrentOperations = current.EffectiveConcurrency;
        ConfirmDestructiveOperations = current.ConfirmDestructiveOperations;
        DeleteToRecycleBin = current.DeleteToRecycleBin;
        LogLevel = current.LogLevel;
    }

    private AppSettings BuildSettings() => new()
    {
        ScanRecursive = ScanRecursive,
        ScanIncludeHidden = ScanIncludeHidden,
        ExportDirectory = ExportDirectory.Trim(),
        MaxConcurrentOperations = ConcurrentOperationsValue,
        ConfirmDestructiveOperations = ConfirmDestructiveOperations,
        DeleteToRecycleBin = DeleteToRecycleBin,
        LogLevel = LogLevel,
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await _settings.SaveAsync(BuildSettings());
            _dialogs.ShowInfo("Settings", "Settings saved.");
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError("Settings", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save settings");
            _dialogs.ShowError("Settings", "Could not save settings: " + ex.Message);
        }
    }

    [RelayCommand]
    private Task ResetToDefaultsAsync()
    {
        var defaults = AppSettings.Defaults();
        ScanRecursive = defaults.ScanRecursive;
        ScanIncludeHidden = defaults.ScanIncludeHidden;
        ExportDirectory = defaults.ExportDirectory;
        MaxConcurrentOperations = defaults.MaxConcurrentOperations;
        ConfirmDestructiveOperations = defaults.ConfirmDestructiveOperations;
        DeleteToRecycleBin = defaults.DeleteToRecycleBin;
        LogLevel = defaults.LogLevel;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var folder = _dialogs.PickFolder(ExportDirectory, "Choose the default export folder");
        if (folder is not null)
        {
            ExportDirectory = folder;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the data folder");
        }
    }
}
