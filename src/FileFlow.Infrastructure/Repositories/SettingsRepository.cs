using FileFlow.Core.Interfaces;
using FileFlow.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Repositories;

/// <summary>SQLite key/value persistence for application settings.</summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                var result = SqliteHelpers.ExecuteScalar(
                    connection,
                    "SELECT value FROM settings WHERE key = $key",
                    new SqliteParameter("$key", key));
                return result as string;
            },
            cancellationToken);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    "INSERT INTO settings (key, value) VALUES ($key, $value) "
                    + "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                    new SqliteParameter("$key", key),
                    new SqliteParameter("$value", value));
            },
            cancellationToken);
    }
}
