using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Database;

/// <summary>
/// Applies pending <see cref="Migrations"/> on startup. Safe to run repeatedly
/// (no-op when the database is current) and on partially migrated databases.
/// </summary>
public sealed class DbMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DbMigrator> _logger;

    public DbMigrator(ISqliteConnectionFactory connectionFactory, ILogger<DbMigrator> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>Brings the schema to the latest version. Returns the new schema version.</summary>
    public int Migrate()
    {
        using var connection = _connectionFactory.OpenConnection();

        // WAL lives outside the migration transactions (it cannot run inside one).
        SqliteHelpers.ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");

        using var createVersionTable = SqliteHelpers.CreateCommand(
            connection,
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL, applied_at TEXT NOT NULL)");
        createVersionTable.ExecuteNonQuery();

        var currentVersion = ReadCurrentVersion(connection);

        foreach (var migration in Migrations.All.OrderBy(m => m.Version))
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            ApplyMigration(connection, migration);
            currentVersion = migration.Version;
            _logger.LogInformation("Applied migration {Version}: {Description}", migration.Version, migration.Description);
        }

        return currentVersion;
    }

    /// <summary>Returns the stored schema version, or 0 for a fresh database.</summary>
    public static int ReadCurrentVersion(SqliteConnection connection)
    {
        var result = SqliteHelpers.ExecuteScalar(connection, "SELECT MAX(version) FROM schema_version");
        return result is long version ? (int)version : 0;
    }

    private static void ApplyMigration(SqliteConnection connection, Migration migration)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var statement in migration.Statements)
            {
                using var command = SqliteHelpers.CreateCommand(connection, statement);
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }

            using var recordVersion = SqliteHelpers.CreateCommand(
                connection,
                "INSERT INTO schema_version (version, applied_at) VALUES ($version, $appliedAt)");
            recordVersion.Transaction = transaction;
            recordVersion.Parameters.AddWithValue("$version", migration.Version);
            recordVersion.Parameters.AddWithValue("$appliedAt", SqliteHelpers.ToDateString(DateTime.UtcNow));
            recordVersion.ExecuteNonQuery();

            transaction.Commit();
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }
}
