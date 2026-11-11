using FileFlow.Core.Interfaces;
using FileFlow.Infrastructure.Database;
using FileFlow.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Repositories;

/// <summary>SQLite persistence for indexed files. All writes run inside transactions.</summary>
public sealed class FileRepository : IFileRepository
{
    private const int UpsertBatchSize = 200;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public FileRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(IReadOnlyCollection<FileRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
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
                    var processed = 0;
                    foreach (var chunk in records.Chunk(UpsertBatchSize))
                    {
                        using var command = BuildUpsertCommand(connection);
                        command.Transaction = transaction;
                        foreach (var record in chunk)
                        {
                            SetUpsertParameters(command, record);
                            command.ExecuteNonQuery();
                        }

                        processed += chunk.Length;
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

    public async Task<FileRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync(
            "SELECT * FROM files WHERE id = $id",
            new SqliteParameter("$id", id),
            cancellationToken);
    }

    public async Task<FileRecord?> GetByPathAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync(
            "SELECT * FROM files WHERE full_path = $path COLLATE NOCASE",
            new SqliteParameter("$path", fullPath),
            cancellationToken);
    }

    public async Task<IReadOnlyList<FileRecord>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<FileRecord>();
        }

        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                var results = new List<FileRecord>(ids.Count);
                foreach (var idBatch in ids.Chunk(200))
                {
                    var parameterNames = idBatch
                        .Select((_, index) => $"$id{index}")
                        .ToArray();
                    var sql = $"SELECT * FROM files WHERE id IN ({string.Join(", ", parameterNames)})";
                    using var command = SqliteHelpers.CreateCommand(connection, sql);
                    for (var i = 0; i < idBatch.Length; i++)
                    {
                        command.Parameters.AddWithValue(parameterNames[i], idBatch[i]);
                    }

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        results.Add(MapRecord(reader));
                    }
                }

                return (IReadOnlyList<FileRecord>)results;
            },
            cancellationToken);
    }

    public async Task UpdatePathAsync(long id, string newFullPath, string newFileName, string newExtension, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    """
                    UPDATE files
                    SET full_path = $path, file_name = $name, extension = $extension
                    WHERE id = $id
                    """,
                    new SqliteParameter("$path", newFullPath),
                    new SqliteParameter("$name", newFileName),
                    new SqliteParameter("$extension", newExtension),
                    new SqliteParameter("$id", id));
            },
            cancellationToken);
    }

    public async Task<(IReadOnlyList<FileListItem> Items, int TotalCount)> SearchAsync(SearchFilter filter, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                var (whereClause, parameters) = BuildWhereClause(filter);

                var countSql = $"SELECT COUNT(*) FROM files f {whereClause}";
                var total = Convert.ToInt64(SqliteHelpers.ExecuteScalar(connection, countSql, parameters));

                var tagsSubquery =
                    "(SELECT GROUP_CONCAT(name, '; ') FROM (SELECT t.name AS name FROM file_tags ft "
                    + "JOIN tags t ON t.id = ft.tag_id WHERE ft.file_id = f.id ORDER BY t.name))";

                var items = new List<FileListItem>();
                if (total > 0)
                {
                    var dataSql =
                        "SELECT f.*, " + tagsSubquery + " AS tags "
                        + $"FROM files f {whereClause} "
                        + "ORDER BY f.modified_at DESC, f.full_path LIMIT $limit OFFSET $offset";
                    using var command = SqliteHelpers.CreateCommand(connection, dataSql, parameters);
                    command.Parameters.AddWithValue("$limit", Math.Clamp(filter.Limit, 1, 100_000));
                    command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Offset));
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var record = MapRecord(reader, prefix: string.Empty);
                        var tags = reader["tags"] as string ?? string.Empty;
                        items.Add(new FileListItem(record, tags));
                    }
                }

                return (Items: (IReadOnlyList<FileListItem>)items, TotalCount: (int)total);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetExtensionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT DISTINCT extension FROM files ORDER BY extension");
                var results = new List<string>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var extension = reader.GetString(0);
                    if (extension.Length > 0)
                    {
                        results.Add(extension);
                    }
                }

                return (IReadOnlyList<string>)results;
            },
            cancellationToken);
    }

    public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
        => await CountAsync("SELECT COUNT(*) FROM files", cancellationToken);

    public async Task<long> SumSizeAsync(CancellationToken cancellationToken = default)
        => await CountLongAsync("SELECT COALESCE(SUM(size_bytes), 0) FROM files", cancellationToken);

    public async Task<int> CountMissingAsync(CancellationToken cancellationToken = default)
        => await CountAsync("SELECT COUNT(*) FROM files WHERE is_missing = 1", cancellationToken);

    public async Task SetMissingAsync(long id, bool missing, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    "UPDATE files SET is_missing = $missing WHERE id = $id",
                    new SqliteParameter("$missing", missing ? 1L : 0L),
                    new SqliteParameter("$id", id));
            },
            cancellationToken);
    }

    public async Task<int> MarkMissingExceptAsync(string rootPath, IReadOnlySet<string> seenPaths, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using (var createTemp = SqliteHelpers.CreateCommand(
                               connection,
                               "CREATE TEMP TABLE IF NOT EXISTS seen_paths (path TEXT PRIMARY KEY)"))
                    {
                        createTemp.Transaction = transaction;
                        createTemp.ExecuteNonQuery();
                    }

                    using (var clearTemp = SqliteHelpers.CreateCommand(connection, "DELETE FROM seen_paths"))
                    {
                        clearTemp.Transaction = transaction;
                        clearTemp.ExecuteNonQuery();
                    }

                    using var insert = SqliteHelpers.CreateCommand(
                        connection,
                        "INSERT OR IGNORE INTO seen_paths (path) VALUES ($path)");
                    insert.Transaction = transaction;
                    foreach (var path in seenPaths)
                    {
                        insert.Parameters.Clear();
                        insert.Parameters.AddWithValue("$path", path);
                        insert.ExecuteNonQuery();
                    }

                    var rootPrefix = rootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                                     + System.IO.Path.DirectorySeparatorChar;
                    using var update = SqliteHelpers.CreateCommand(
                        connection,
                        """
                        UPDATE files SET is_missing = 1
                        WHERE is_missing = 0
                          AND full_path LIKE $rootPrefix ESCAPE '\'
                          AND full_path NOT IN (SELECT path FROM seen_paths)
                        """);
                    update.Transaction = transaction;
                    update.Parameters.AddWithValue(
                        "$rootPrefix",
                        SqliteHelpers.EscapeLike(rootPrefix) + "%");
                    var affected = update.ExecuteNonQuery();

                    transaction.Commit();
                    return affected;
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            },
            cancellationToken);
    }

    private async Task<int> CountAsync(string sql, CancellationToken cancellationToken)
        => await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return Convert.ToInt32(SqliteHelpers.ExecuteScalar(connection, sql));
            },
            cancellationToken);

    private async Task<long> CountLongAsync(string sql, CancellationToken cancellationToken)
        => await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return Convert.ToInt64(SqliteHelpers.ExecuteScalar(connection, sql));
            },
            cancellationToken);

    private async Task<FileRecord?> QuerySingleAsync(string sql, SqliteParameter parameter, CancellationToken cancellationToken)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(connection, sql, parameter);
                using var reader = command.ExecuteReader();
                return reader.Read() ? MapRecord(reader) : null;
            },
            cancellationToken);
    }

    private static (string WhereClause, SqliteParameter[] Parameters) BuildWhereClause(SearchFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        var keyword = filter.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            var pattern = "%" + SqliteHelpers.EscapeLike(keyword) + "%";
            conditions.Add("(f.file_name LIKE $keyword ESCAPE '\\' OR f.full_path LIKE $keyword ESCAPE '\\')");
            parameters.Add(new SqliteParameter("$keyword", pattern));
        }

        if (!string.IsNullOrEmpty(filter.Extension))
        {
            conditions.Add("f.extension = $extension");
            parameters.Add(new SqliteParameter("$extension", filter.Extension.ToLowerInvariant()));
        }

        if (filter.MinSizeBytes is not null)
        {
            conditions.Add("f.size_bytes >= $minSize");
            parameters.Add(new SqliteParameter("$minSize", filter.MinSizeBytes.Value));
        }

        if (filter.MaxSizeBytes is not null)
        {
            conditions.Add("f.size_bytes <= $maxSize");
            parameters.Add(new SqliteParameter("$maxSize", filter.MaxSizeBytes.Value));
        }

        if (filter.ModifiedFromUtc is not null)
        {
            conditions.Add("f.modified_at >= $modifiedFrom");
            parameters.Add(new SqliteParameter("$modifiedFrom", SqliteHelpers.ToDateString(filter.ModifiedFromUtc.Value)));
        }

        if (filter.ModifiedToUtc is not null)
        {
            conditions.Add("f.modified_at <= $modifiedTo");
            parameters.Add(new SqliteParameter("$modifiedTo", SqliteHelpers.ToDateString(filter.ModifiedToUtc.Value)));
        }

        if (filter.Missing is not null)
        {
            conditions.Add("f.is_missing = $missing");
            parameters.Add(new SqliteParameter("$missing", filter.Missing.Value ? 1L : 0L));
        }

        if (!string.IsNullOrEmpty(filter.RootPath))
        {
            var rootPrefix = filter.RootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                             + System.IO.Path.DirectorySeparatorChar;
            conditions.Add("f.full_path LIKE $rootPrefix ESCAPE '\\'");
            parameters.Add(new SqliteParameter("$rootPrefix", SqliteHelpers.EscapeLike(rootPrefix) + "%"));
        }

        if (filter.TagIds.Count > 0)
        {
            var parameterNames = new string[filter.TagIds.Count];
            for (var i = 0; i < filter.TagIds.Count; i++)
            {
                parameterNames[i] = $"$tag{i}";
                parameters.Add(new SqliteParameter(parameterNames[i], filter.TagIds[i]));
            }

            conditions.Add(
                $"(SELECT COUNT(DISTINCT ft.tag_id) FROM file_tags ft WHERE ft.file_id = f.id "
                + $"AND ft.tag_id IN ({string.Join(", ", parameterNames)})) = $tagCount");
            parameters.Add(new SqliteParameter("$tagCount", filter.TagIds.Count));
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        return (whereClause, parameters.ToArray());
    }

    private static SqliteCommand BuildUpsertCommand(SqliteConnection connection)
        => SqliteHelpers.CreateCommand(
            connection,
            """
            INSERT INTO files (full_path, file_name, extension, size_bytes, created_at, modified_at, scanned_at, content_hash, is_missing)
            VALUES ($path, $name, $extension, $size, $createdAt, $modifiedAt, $scannedAt, $contentHash, 0)
            ON CONFLICT(full_path) DO UPDATE SET
                file_name = excluded.file_name,
                extension = excluded.extension,
                size_bytes = excluded.size_bytes,
                created_at = excluded.created_at,
                modified_at = excluded.modified_at,
                scanned_at = excluded.scanned_at,
                is_missing = 0
            """);

    private static void SetUpsertParameters(SqliteCommand command, FileRecord record)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$path", record.FullPath);
        command.Parameters.AddWithValue("$name", record.FileName);
        command.Parameters.AddWithValue("$extension", record.Extension);
        command.Parameters.AddWithValue("$size", record.SizeBytes);
        command.Parameters.AddWithValue("$createdAt", SqliteHelpers.ToDateString(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$modifiedAt", SqliteHelpers.ToDateString(record.ModifiedAtUtc));
        command.Parameters.AddWithValue("$scannedAt", SqliteHelpers.ToDateString(record.ScannedAtUtc));
        command.Parameters.AddWithValue("$contentHash", (object?)record.ContentHash ?? DBNull.Value);
    }

    private static FileRecord MapRecord(SqliteDataReader reader, string prefix = "")
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal(prefix + "id")),
            FullPath = reader.GetString(reader.GetOrdinal(prefix + "full_path")),
            FileName = reader.GetString(reader.GetOrdinal(prefix + "file_name")),
            Extension = reader.GetString(reader.GetOrdinal(prefix + "extension")),
            SizeBytes = reader.GetInt64(reader.GetOrdinal(prefix + "size_bytes")),
            CreatedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal(prefix + "created_at")]),
            ModifiedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal(prefix + "modified_at")]),
            ScannedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal(prefix + "scanned_at")]),
            ContentHash = reader.IsDBNull(reader.GetOrdinal(prefix + "content_hash"))
                ? null
                : reader.GetString(reader.GetOrdinal(prefix + "content_hash")),
            IsMissing = reader.GetInt64(reader.GetOrdinal(prefix + "is_missing")) != 0,
        };
}
