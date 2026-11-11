using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;
using FileFlow.App.Services;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Localization;
using FileFlow.Core.Models;
using FileFlow.Core.Validation;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>A selectable interface language.</summary>
public sealed record LanguageOption(string Code, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Settings page: edits a working copy and saves through ISettingsService.</summary>
public partial class SettingsViewModel : ObservableObject, IRefreshable
{
    private static readonly IReadOnlyList<string> LogLevels = ["Trace", "Debug", "Information", "Warning", "Error"];

    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _initializing;

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

        _initializing = true;
        foreach (var code in AppLanguages.All)
        {
            LanguageOptions.Add(new LanguageOption(code, DisplayNames.Language(code)));
        }

        LoadFromCurrent();
        _initializing = false;
    }

    public ObservableCollection<string> LogLevelOptions { get; } = new(LogLevels);

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();

    public string DataDirectoryText => _paths.DataDirectory;

    public string DatabasePathText => _paths.DatabasePath;

    public string LogsDirectoryText => _paths.LogsDirectory;

    public string LogFolderText => Loc.Instance.F("Settings_LogFolder", _paths.LogsDirectory);

    public string DatabaseText => Loc.Instance.F("Settings_Database", _paths.DatabasePath);

    public string DataFolderText => Loc.Instance.F("Settings_DataFolder", _paths.DataDirectory);

    [ObservableProperty]
    private LanguageOption? selectedLanguage;

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
        RefreshLocalization();
        return Task.CompletedTask;
    }

    /// <summary>Re-reads the generated labels after a language change.</summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LogFolderText));
        OnPropertyChanged(nameof(DatabaseText));
        OnPropertyChanged(nameof(DataFolderText));
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

        var language = AppLanguages.Normalize(current.UiLanguage);
        SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == language) ?? LanguageOptions[0];
    }

    /// <summary>Switching the language applies immediately and is persisted right away.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_initializing || value is null || value.Code == Loc.Instance.Language)
        {
            return;
        }

        Loc.Instance.SetLanguage(value.Code);
        _ = PersistLanguageAsync(value.Code);
    }

    private async Task PersistLanguageAsync(string code)
    {
        try
        {
            var settings = BuildSettings();
            settings.UiLanguage = code;
            await _settings.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist the interface language");
        }
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
        UiLanguage = SelectedLanguage?.Code ?? AppLanguages.Default,
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await _settings.SaveAsync(BuildSettings());
            _dialogs.ShowInfo(Loc.Instance["Settings_Title"], Loc.Instance["Settings_Saved"]);
        }
        catch (FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Settings_Title"], ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save the settings");
            _dialogs.ShowError(Loc.Instance["Settings_Title"], Loc.Instance.F("Settings_SaveError", ex.Message));
        }
    }

    [RelayCommand]
    private Task ResetToDefaultsAsync()
    {
        var defaults = AppSettings.Defaults();
        _initializing = true;
        ScanRecursive = defaults.ScanRecursive;
        ScanIncludeHidden = defaults.ScanIncludeHidden;
        ExportDirectory = defaults.ExportDirectory;
        MaxConcurrentOperations = defaults.MaxConcurrentOperations;
        ConfirmDestructiveOperations = defaults.ConfirmDestructiveOperations;
        DeleteToRecycleBin = defaults.DeleteToRecycleBin;
        LogLevel = defaults.LogLevel;
        _initializing = false;

        // The interface language is a display preference: resetting the other settings keeps it.
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var folder = _dialogs.PickFolder(ExportDirectory, Loc.Instance["Settings_PickExportFolder"]);
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
