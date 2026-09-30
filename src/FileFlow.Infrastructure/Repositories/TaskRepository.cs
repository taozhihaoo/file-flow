using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Infrastructure.Database;
using FileFlow.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Repositories;

/// <summary>SQLite persistence for the task queue and its items.</summary>
public sealed class TaskRepository : ITaskRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public TaskRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateAsync(FileTask task, IReadOnlyList<TaskItemRecord> items, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    task.CreatedAtUtc = DateTime.UtcNow;
                    task.Total = items.Count;
                    using var insertTask = SqliteHelpers.CreateCommand(
                        connection,
                        """
                        INSERT INTO tasks (type, status, created_at, total, processed, succeeded, failed, error_count, description, metadata)
                        VALUES ($type, $status, $createdAt, $total, 0, 0, 0, 0, $description, $metadata);
                        SELECT last_insert_rowid();
                        """);
                    insertTask.Transaction = transaction;
                    insertTask.Parameters.AddWithValue("$type", task.Type.ToString());
                    insertTask.Parameters.AddWithValue("$status", task.State.ToString());
                    insertTask.Parameters.AddWithValue("$createdAt", SqliteHelpers.ToDateString(task.CreatedAtUtc));
                    insertTask.Parameters.AddWithValue("$total", task.Total);
                    insertTask.Parameters.AddWithValue("$description", (object?)task.Description ?? DBNull.Value);
                    insertTask.Parameters.AddWithValue("$metadata", (object?)task.Metadata ?? DBNull.Value);
                    var taskId = Convert.ToInt64(insertTask.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

                    using var insertItem = SqliteHelpers.CreateCommand(
                        connection,
                        """
                        INSERT INTO task_items (task_id, source_path, target_path, status, error_kind)
                        VALUES ($taskId, $source, $target, $status, $errorKind)
                        """);
                    insertItem.Transaction = transaction;
                    foreach (var item in items)
                    {
                        item.TaskId = taskId;
                        insertItem.Parameters.Clear();
                        insertItem.Parameters.AddWithValue("$taskId", taskId);
                        insertItem.Parameters.AddWithValue("$source", item.SourcePath);
                        insertItem.Parameters.AddWithValue("$target", (object?)item.TargetPath ?? DBNull.Value);
                        insertItem.Parameters.AddWithValue("$status", item.State.ToString());
                        insertItem.Parameters.AddWithValue("$errorKind", item.ErrorKind.ToString());
                        insertItem.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return taskId;
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            },
            cancellationToken);
    }

    public async Task<FileTask?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT * FROM tasks WHERE id = $id",
                    new SqliteParameter("$id", id));
                using var reader = command.ExecuteReader();
                return reader.Read() ? MapTask(reader) : null;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(FileTask task, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    """
                    UPDATE tasks
                    SET status = $status,
                        started_at = $startedAt,
                        completed_at = $completedAt,
                        processed = $processed,
                        succeeded = $succeeded,
                        failed = $failed,
                        error_count = $errorCount,
                        metadata = $metadata
                    WHERE id = $id
                    """,
                    new SqliteParameter("$status", task.State.ToString()),
                    new SqliteParameter("$startedAt", task.StartedAtUtc is null ? DBNull.Value : SqliteHelpers.ToDateString(task.StartedAtUtc.Value)),
                    new SqliteParameter("$completedAt", task.CompletedAtUtc is null ? DBNull.Value : SqliteHelpers.ToDateString(task.CompletedAtUtc.Value)),
                    new SqliteParameter("$processed", task.Processed),
                    new SqliteParameter("$succeeded", task.Succeeded),
                    new SqliteParameter("$failed", task.Failed),
                    new SqliteParameter("$errorCount", task.ErrorCount),
                    new SqliteParameter("$metadata", (object?)task.Metadata ?? DBNull.Value),
                    new SqliteParameter("$id", task.Id));
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<FileTask>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT * FROM tasks ORDER BY created_at DESC, id DESC LIMIT $limit",
                    new SqliteParameter("$limit", Math.Clamp(limit, 1, 1000)));
                var results = new List<FileTask>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapTask(reader));
                }

                return (IReadOnlyList<FileTask>)results;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<FileTask>> GetByStateAsync(TaskState state, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT * FROM tasks WHERE status = $status ORDER BY created_at",
                    new SqliteParameter("$status", state.ToString()));
                var results = new List<FileTask>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapTask(reader));
                }

                return (IReadOnlyList<FileTask>)results;
            },
            cancellationToken);
    }

    public async Task<int> MarkRunningAsInterruptedAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return SqliteHelpers.ExecuteNonQuery(
                    connection,
                    "UPDATE tasks SET status = 'Interrupted', completed_at = $now WHERE status = 'Running'",
                    new SqliteParameter("$now", SqliteHelpers.ToDateString(DateTime.UtcNow)));
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<TaskItemRecord>> GetItemsAsync(long taskId, CancellationToken cancellationToken = default)
        => await QueryItemsAsync(
            "SELECT * FROM task_items WHERE task_id = $taskId ORDER BY id",
            new SqliteParameter("$taskId", taskId),
            cancellationToken);

    public async Task<IReadOnlyList<TaskItemRecord>> GetFailedItemsAsync(long taskId, CancellationToken cancellationToken = default)
        => await QueryItemsAsync(
            "SELECT * FROM task_items WHERE task_id = $taskId AND status = 'Failed' ORDER BY id",
            new SqliteParameter("$taskId", taskId),
            cancellationToken);

    public async Task UpdateItemAsync(TaskItemRecord item, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                SqliteHelpers.ExecuteNonQuery(
                    connection,
                    """
                    UPDATE task_items
                    SET status = $status,
                        error_kind = $errorKind,
                        error_message = $errorMessage,
                        processed_at = $processedAt
                    WHERE id = $id
                    """,
                    new SqliteParameter("$status", item.State.ToString()),
                    new SqliteParameter("$errorKind", item.ErrorKind.ToString()),
                    new SqliteParameter("$errorMessage", (object?)item.ErrorMessage ?? DBNull.Value),
                    new SqliteParameter("$processedAt", item.ProcessedAtUtc is null ? DBNull.Value : SqliteHelpers.ToDateString(item.ProcessedAtUtc.Value)),
                    new SqliteParameter("$id", item.Id));
            },
            cancellationToken);
    }

    public async Task<int> CancelPendingItemsAsync(long taskId, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return SqliteHelpers.ExecuteNonQuery(
                    connection,
                    "UPDATE task_items SET status = 'Cancelled', processed_at = $now WHERE task_id = $taskId AND status = 'Pending'",
                    new SqliteParameter("$now", SqliteHelpers.ToDateString(DateTime.UtcNow)),
                    new SqliteParameter("$taskId", taskId));
            },
            cancellationToken);
    }

    public async Task<int> DeleteFinishedAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return SqliteHelpers.ExecuteNonQuery(
                    connection,
                    """
                    DELETE FROM tasks WHERE status IN (
                        'Completed', 'CompletedWithErrors', 'Failed', 'Cancelled', 'Interrupted')
                    """);
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<TaskItemRecord>> QueryItemsAsync(string sql, SqliteParameter parameter, CancellationToken cancellationToken)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(connection, sql, parameter);
                var results = new List<TaskItemRecord>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapItem(reader));
                }

                return (IReadOnlyList<TaskItemRecord>)results;
            },
            cancellationToken);
    }

    private static FileTask MapTask(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Type = Enum.Parse<TaskType>(reader.GetString(reader.GetOrdinal("type"))),
            State = Enum.Parse<TaskState>(reader.GetString(reader.GetOrdinal("status"))),
            CreatedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal("created_at")]),
            StartedAtUtc = reader.IsDBNull(reader.GetOrdinal("started_at"))
                ? null
                : SqliteHelpers.FromDateString(reader[reader.GetOrdinal("started_at")]),
            CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("completed_at"))
                ? null
                : SqliteHelpers.FromDateString(reader[reader.GetOrdinal("completed_at")]),
            Total = Convert.ToInt32(reader[reader.GetOrdinal("total")], System.Globalization.CultureInfo.InvariantCulture),
            Processed = Convert.ToInt32(reader[reader.GetOrdinal("processed")], System.Globalization.CultureInfo.InvariantCulture),
            Succeeded = Convert.ToInt32(reader[reader.GetOrdinal("succeeded")], System.Globalization.CultureInfo.InvariantCulture),
            Failed = Convert.ToInt32(reader[reader.GetOrdinal("failed")], System.Globalization.CultureInfo.InvariantCulture),
            ErrorCount = Convert.ToInt32(reader[reader.GetOrdinal("error_count")], System.Globalization.CultureInfo.InvariantCulture),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            Metadata = reader.IsDBNull(reader.GetOrdinal("metadata")) ? null : reader.GetString(reader.GetOrdinal("metadata")),
        };

    private static TaskItemRecord MapItem(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            TaskId = reader.GetInt64(reader.GetOrdinal("task_id")),
            SourcePath = reader.GetString(reader.GetOrdinal("source_path")),
            TargetPath = reader.IsDBNull(reader.GetOrdinal("target_path")) ? null : reader.GetString(reader.GetOrdinal("target_path")),
            State = Enum.Parse<TaskItemState>(reader.GetString(reader.GetOrdinal("status"))),
            ErrorKind = Enum.TryParse<ErrorKind>(reader.GetString(reader.GetOrdinal("error_kind")), out var kind) ? kind : ErrorKind.Unknown,
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
            ProcessedAtUtc = reader.IsDBNull(reader.GetOrdinal("processed_at"))
                ? null
                : SqliteHelpers.FromDateString(reader[reader.GetOrdinal("processed_at")]),
        };
}
