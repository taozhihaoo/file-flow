using FileFlow.Core.Interfaces;
using FileFlow.Infrastructure.Database;
using FileFlow.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Repositories;

/// <summary>SQLite persistence for the operation history.</summary>
public sealed class HistoryRepository : IHistoryRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public HistoryRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AddAsync(OperationRecord record, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    """
                    INSERT INTO operation_history (task_id, operation_type, description, item_count, success_count, failure_count, metadata, created_at)
                    VALUES ($taskId, $type, $description, $itemCount, $successCount, $failureCount, $metadata, $createdAt)
                    """,
                    new SqliteParameter("$taskId", record.TaskId is null ? DBNull.Value : record.TaskId),
                    new SqliteParameter("$type", record.OperationType),
                    new SqliteParameter("$description", (object?)record.Description ?? DBNull.Value),
                    new SqliteParameter("$itemCount", record.ItemCount),
                    new SqliteParameter("$successCount", record.SuccessCount),
                    new SqliteParameter("$failureCount", record.FailureCount),
                    new SqliteParameter("$metadata", (object?)record.Metadata ?? DBNull.Value),
                    new SqliteParameter("$createdAt", SqliteHelpers.ToDateString(record.CreatedAtUtc == default ? DateTime.UtcNow : record.CreatedAtUtc)));
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    public async Task<(IReadOnlyList<OperationRecord> Items, int TotalCount)> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                var total = Convert.ToInt32(
                    SqliteHelpers.ExecuteScalar(connection, "SELECT COUNT(*) FROM operation_history"),
                    System.Globalization.CultureInfo.InvariantCulture);

                var items = new List<OperationRecord>();
                if (total > 0)
                {
                    using var command = SqliteHelpers.CreateCommand(
                        connection,
                        "SELECT * FROM operation_history ORDER BY created_at DESC, id DESC LIMIT $limit OFFSET $offset",
                        new SqliteParameter("$limit", Math.Clamp(limit, 1, 1000)),
                        new SqliteParameter("$offset", Math.Max(0, offset)));
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        items.Add(MapRecord(reader));
                    }
                }

                return (Items: (IReadOnlyList<OperationRecord>)items, TotalCount: total);
            },
            cancellationToken);
    }

    public async Task<int> CountFailedOperationsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                return Convert.ToInt32(
                    SqliteHelpers.ExecuteScalar(
                        connection,
                        "SELECT COUNT(*) FROM operation_history WHERE failure_count > 0"),
                    System.Globalization.CultureInfo.InvariantCulture);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<OperationRecord>> GetByTypeAsync(string operationType, int limit, CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                using var connection = _connectionFactory.OpenConnection();
                using var command = SqliteHelpers.CreateCommand(
                    connection,
                    "SELECT * FROM operation_history WHERE operation_type = $type ORDER BY created_at DESC, id DESC LIMIT $limit",
                    new SqliteParameter("$type", operationType),
                    new SqliteParameter("$limit", Math.Clamp(limit, 1, 1000)));
                var results = new List<OperationRecord>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(MapRecord(reader));
                }

                return (IReadOnlyList<OperationRecord>)results;
            },
            cancellationToken);
    }

    private static OperationRecord MapRecord(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            TaskId = reader.IsDBNull(reader.GetOrdinal("task_id")) ? null : reader.GetInt64(reader.GetOrdinal("task_id")),
            OperationType = reader.GetString(reader.GetOrdinal("operation_type")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            ItemCount = Convert.ToInt32(reader[reader.GetOrdinal("item_count")], System.Globalization.CultureInfo.InvariantCulture),
            SuccessCount = Convert.ToInt32(reader[reader.GetOrdinal("success_count")], System.Globalization.CultureInfo.InvariantCulture),
            FailureCount = Convert.ToInt32(reader[reader.GetOrdinal("failure_count")], System.Globalization.CultureInfo.InvariantCulture),
            Metadata = reader.IsDBNull(reader.GetOrdinal("metadata")) ? null : reader.GetString(reader.GetOrdinal("metadata")),
            CreatedAtUtc = SqliteHelpers.FromDateString(reader[reader.GetOrdinal("created_at")]),
        };
}
