using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Infrastructure.Database;
using FileFlow.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Repositories;

/// <summary>SQLite persistence for tags and file-tag relations.</summary>
public sealed class TagRepository : ITagRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public TagRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    """
                    SELECT t.id, t.name, t.color, t.created_at,
                           (SELECT COUNT(*) FROM file_tags ft WHERE ft.tag_id = t.id) AS file_count
                    FROM tags t
                    ORDER BY t.name COLLATE NOCASE
                    """);
                var results = new List<Tag>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapTag(reader));
                }

                return (IReadOnlyList<Tag>)results;
            },
            cancellationToken);
    }

    public async Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT id, name, color, created_at, 0 AS file_count FROM tags WHERE name = $name COLLATE NOCASE",
                    new SqliteParameter("$name", name));
                using var reader = command.ExecuteReader();
                return reader.Read() ? MapTag(reader) : null;
            },
            cancellationToken);
    }

    public async Task<Tag> CreateAsync(string name, string? color = null, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? throw new ArgumentNullException(nameof(name));
        if (trimmed.Length == 0)
        {
            throw FileFlowException.PathValidation("Tag name cannot be empty.");
        }

        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                try
                {
                    using var command = SqliteHelpers.CreateCommand(
                        connection,
                        "INSERT INTO tags (name, color, created_at) VALUES ($name, $color, $createdAt); SELECT last_insert_rowid();",
                        new SqliteParameter("$name", trimmed),
                        new SqliteParameter("$color", (object?)color ?? DBNull.Value),
                        new SqliteParameter("$createdAt", SqliteHelpers.ToDateString(DateTime.UtcNow)));
                    var id = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                    return new Tag { Id = id, Name = trimmed, Color = color, CreatedAtUtc = DateTime.UtcNow };
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    throw FileFlowException.Conflict($"A tag named '{trimmed}' already exists.", ex);
                }
            },
            cancellationToken);
    }

    public async Task RenameAsync(long tagId, string newName, CancellationToken cancellationToken = default)
    {
        var trimmed = newName?.Trim() ?? throw new ArgumentNullException(nameof(newName));
        if (trimmed.Length == 0)
        {
            throw FileFlowException.PathValidation("Tag name cannot be empty.");
        }

        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                try
                {
                    SqliteHelpers.ExecuteNonQuery(
                        connection,
                        "UPDATE tags SET name = $name WHERE id = $id",
                        new SqliteParameter("$name", trimmed),
                        new SqliteParameter("$id", tagId));
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    throw FileFlowException.Conflict($"A tag named '{trimmed}' already exists.", ex);
                }
            },
            cancellationToken);
    }

    public async Task DeleteAsync(long tagId, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    "DELETE FROM tags WHERE id = $id",
                    new SqliteParameter("$id", tagId));
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetTagsForFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    """
                    SELECT t.id, t.name, t.color, t.created_at, 0 AS file_count
                    FROM file_tags ft JOIN tags t ON t.id = ft.tag_id
                    WHERE ft.file_id = $fileId
                    ORDER BY t.name COLLATE NOCASE
                    """,
                    new SqliteParameter("$fileId", fileId));
                var results = new List<Tag>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapTag(reader));
                }

                return (IReadOnlyList<Tag>)results;
            },
            cancellationToken);
    }

    public async Task AddTagToFilesAsync(long tagId, IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default)
    {
        if (fileIds.Count == 0)
        {
            return;
        }

        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using var command = SqliteHelpers.CreateCommand(
                        connection,
                        "INSERT OR IGNORE INTO file_tags (file_id, tag_id) VALUES ($fileId, $tagId)");
                    command.Transaction = transaction;
                    foreach (var fileId in fileIds)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("$fileId", fileId);
                        command.Parameters.AddWithValue("$tagId", tagId);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            },
            cancellationToken);
    }

    public async Task RemoveTagFromFilesAsync(long tagId, IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default)
    {
        if (fileIds.Count == 0)
        {
            return;
        }

        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using var command = SqliteHelpers.CreateCommand(
                        connection,
                        "DELETE FROM file_tags WHERE tag_id = $tagId AND file_id = $fileId");
                    command.Transaction = transaction;
                    foreach (var fileId in fileIds)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("$fileId", fileId);
                        command.Parameters.AddWithValue("$tagId", tagId);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            },
            cancellationToken);
    }

    private static Tag MapTag(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString(reader.GetOrdinal("color")),
            CreatedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal("created_at")]),
            FileCount = Convert.ToInt32(reader[reader.GetOrdinal("file_count")], System.Globalization.CultureInfo.InvariantCulture),
        };
}
