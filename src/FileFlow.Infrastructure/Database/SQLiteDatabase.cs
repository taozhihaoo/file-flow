using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Database;

/// <summary>Creates short-lived, pragma-configured SQLite connections.</summary>
public interface ISqliteConnectionFactory
{
    SqliteConnection OpenConnection();
}

/// <summary>
/// Connection factory for the application database
/// (default location: %LOCALAPPDATA%\FileFlow\fileflow.db).
/// Connections are pooled by Microsoft.Data.Sqlite; pragmas that are per-connection
/// (foreign keys, busy timeout) are applied on every open.
/// </summary>
public sealed class SQLiteDatabase : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SQLiteDatabase(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        SqliteHelpers.ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        SqliteHelpers.ExecuteNonQuery(connection, "PRAGMA busy_timeout = 5000;");
        return connection;
    }
}
