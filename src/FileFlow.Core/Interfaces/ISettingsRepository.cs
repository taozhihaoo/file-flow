namespace FileFlow.Core.Interfaces;

/// <summary>Key/value persistence for application settings.</summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}
