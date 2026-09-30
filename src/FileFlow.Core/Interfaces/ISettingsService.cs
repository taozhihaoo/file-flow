using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>
/// Central configuration abstraction. Business logic reads settings only through this
/// interface - never from environment variables or hardcoded values.
/// </summary>
public interface ISettingsService
{
    /// <summary>Current settings (defaults merged with stored user settings).</summary>
    AppSettings Current { get; }

    /// <summary>Loads stored settings; falls back to defaults for missing values.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates and persists settings, then updates <see cref="Current"/>.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Raised on the caller's thread after settings were saved.</summary>
    event EventHandler? Saved;
}
