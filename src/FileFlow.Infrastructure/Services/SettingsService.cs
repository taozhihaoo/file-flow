using System.Text.Json;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Localization;
using FileFlow.Core.Models;
using FileFlow.Core.Validation;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> in the SQLite settings table.
/// Missing values fall back to defaults; saved values are validated and clamped.
/// This is the single configuration entry point - no component reads raw environment values.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string StorageKey = "app_settings";

    private static readonly IReadOnlyList<string> ValidLogLevels = ["Trace", "Debug", "Information", "Warning", "Error"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly ISettingsRepository _repository;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ISettingsRepository repository, ILogger<SettingsService> logger)
    {
        _repository = repository;
        _logger = logger;
        Current = AppSettings.Defaults();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Saved;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _repository.GetAsync(StorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            Current = AppSettings.Defaults();
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            Current = stored is null ? AppSettings.Defaults() : Normalize(stored);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stored settings are not valid JSON; using defaults");
            Current = AppSettings.Defaults();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        var json = JsonSerializer.Serialize(normalized, SerializerOptions);
        await _repository.SetAsync(StorageKey, json, cancellationToken);
        Current = normalized;
        _logger.LogInformation("Settings saved");
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.MaxConcurrentOperations = Math.Clamp(
            settings.MaxConcurrentOperations,
            AppSettings.MinConcurrency,
            AppSettings.MaxConcurrency);

        var level = ValidLogLevels.FirstOrDefault(
            l => string.Equals(l, settings.LogLevel, StringComparison.OrdinalIgnoreCase));
        settings.LogLevel = level ?? "Information";

        settings.UiLanguage = AppLanguages.Normalize(settings.UiLanguage);

        if (string.IsNullOrWhiteSpace(settings.ExportDirectory))
        {
            settings.ExportDirectory = string.Empty;
        }
        else
        {
            var error = PathValidator.ValidateDirectoryPath(settings.ExportDirectory);
            if (error is not null)
            {
                throw FileFlowException.PathValidation($"Export directory: {error}");
            }
        }

        return settings;
    }
}
