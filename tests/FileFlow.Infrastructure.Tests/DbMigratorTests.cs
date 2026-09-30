using Xunit;
using FileFlow.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

public class DbMigratorTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public void Migrate_CreatesAllTables()
    {
        using var connection = _db.Factory.OpenConnection();
        foreach (var table in new[] { "files", "tags", "file_tags", "tasks", "task_items", "operation_history", "settings" })
        {
            var result = SqliteHelpers.ExecuteScalar(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name",
                new SqliteParameter("$name", table));
            Assert.Equal(1L, result);
        }
    }

    [Fact]
    public void Migrate_IsRepeatable()
    {
        var first = new DbMigrator(_db.Factory, NullLogger<DbMigrator>.Instance).Migrate();
        var second = new DbMigrator(_db.Factory, NullLogger<DbMigrator>.Instance).Migrate();

        Assert.Equal(first, second);
        using var connection = _db.Factory.OpenConnection();
        Assert.Equal(1L, SqliteHelpers.ExecuteScalar(connection, "SELECT MAX(version) FROM schema_version"));
    }

    [Fact]
    public void Migrate_PreservesExistingData()
    {
        using (var connection = _db.Factory.OpenConnection())
        {
            SqliteHelpers.ExecuteNonQuery(
                connection,
                "INSERT INTO tags (name, created_at) VALUES ('Keep', $now)",
                new SqliteParameter("$now", SqliteHelpers.ToDateString(DateTime.UtcNow)));
        }

        new DbMigrator(_db.Factory, NullLogger<DbMigrator>.Instance).Migrate();

        using (var connection = _db.Factory.OpenConnection())
        {
            Assert.Equal(1L, SqliteHelpers.ExecuteScalar(connection, "SELECT COUNT(*) FROM tags WHERE name = 'Keep'"));
        }
    }

    [Fact]
    public void Migrate_EnablesWalJournaling()
    {
        using var connection = _db.Factory.OpenConnection();
        var mode = SqliteHelpers.ExecuteScalar(connection, "PRAGMA journal_mode") as string;
        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public void ForeignKeys_CascadeOnTagDelete()
    {
        using (var connection = _db.Factory.OpenConnection())
        {
            var now = SqliteHelpers.ToDateString(DateTime.UtcNow);
            SqliteHelpers.ExecuteNonQuery(connection, "INSERT INTO files (full_path, file_name, created_at, modified_at, scanned_at) VALUES ('C:\\a.txt', 'a.txt', $n, $n, $n)", new SqliteParameter("$n", now));
            SqliteHelpers.ExecuteNonQuery(connection, "INSERT INTO tags (name, created_at) VALUES ('t1', $n)", new SqliteParameter("$n", now));
            SqliteHelpers.ExecuteNonQuery(connection, "INSERT INTO file_tags (file_id, tag_id) SELECT 1, 1");
            Assert.Equal(1L, SqliteHelpers.ExecuteScalar(connection, "SELECT COUNT(*) FROM file_tags"));

            SqliteHelpers.ExecuteNonQuery(connection, "DELETE FROM tags WHERE id = 1");
            Assert.Equal(0L, SqliteHelpers.ExecuteScalar(connection, "SELECT COUNT(*) FROM file_tags"));
        }
    }

    public void Dispose() => _db.Dispose();
}
