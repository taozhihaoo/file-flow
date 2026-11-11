namespace FileFlow.Core.Models;

/// <summary>
/// User configurable application settings. Persisted in the SQLite <c>settings</c> table
/// under the user's AppData directory; never in the program directory.
/// </summary>
public sealed class AppSettings
{
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 16;
    public const int DefaultConcurrency = 4;

    /// <summary>Default value for the "include subfolders" option in the scan panel.</summary>
    public bool ScanRecursive { get; set; } = true;

    /// <summary>Default value for the "include hidden files" option in the scan panel.</summary>
    public bool ScanIncludeHidden { get; set; }

    /// <summary>Directory where CSV exports are saved by default. Empty means the user's Documents folder.</summary>
    public string ExportDirectory { get; set; } = string.Empty;

    /// <summary>Bounded parallelism for task execution. Clamped to <see cref="MinConcurrency"/>..<see cref="MaxConcurrency"/>.</summary>
    public int MaxConcurrentOperations { get; set; } = DefaultConcurrency;

    /// <summary>When true, delete/move operations always ask for confirmation first.</summary>
    public bool ConfirmDestructiveOperations { get; set; } = true;

    /// <summary>When true, deleted files go to the Windows Recycle Bin instead of being removed permanently.</summary>
    public bool DeleteToRecycleBin { get; set; } = true;

    /// <summary>Minimum level for file logging: Trace, Debug, Information, Warning, Error.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Interface language: "en" (default) or "zh-CN". Unknown values fall back to English.</summary>
    public string UiLanguage { get; set; } = Localization.AppLanguages.Default;

    public static AppSettings Defaults() => new();

    public AppSettings Clone() => new()
    {
        ScanRecursive = ScanRecursive,
        ScanIncludeHidden = ScanIncludeHidden,
        ExportDirectory = ExportDirectory,
        MaxConcurrentOperations = MaxConcurrentOperations,
        ConfirmDestructiveOperations = ConfirmDestructiveOperations,
        DeleteToRecycleBin = DeleteToRecycleBin,
        LogLevel = LogLevel,
        UiLanguage = UiLanguage,
    };

    public int EffectiveConcurrency => Math.Clamp(MaxConcurrentOperations, MinConcurrency, MaxConcurrency);
}
