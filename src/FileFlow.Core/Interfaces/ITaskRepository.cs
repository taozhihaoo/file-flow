using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Persistence for the task queue.</summary>
public interface ITaskRepository
{
    /// <summary>Creates the task and all its items in one transaction; returns the task id.</summary>
    Task<long> CreateAsync(FileTask task, IReadOnlyList<TaskItemRecord> items, CancellationToken cancellationToken = default);

    Task<FileTask?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Updates state, counters and timestamps.</summary>
    Task UpdateAsync(FileTask task, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileTask>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileTask>> GetByStateAsync(TaskState state, CancellationToken cancellationToken = default);

    /// <summary>Crash recovery: marks every task left in Running as Interrupted. Returns the number of affected tasks.</summary>
    Task<int> MarkRunningAsInterruptedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItemRecord>> GetItemsAsync(long taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItemRecord>> GetFailedItemsAsync(long taskId, CancellationToken cancellationToken = default);

    Task UpdateItemAsync(TaskItemRecord item, CancellationToken cancellationToken = default);

    /// <summary>Marks all still-pending items of a task as cancelled (used when a task is cancelled).</summary>
    Task<int> CancelPendingItemsAsync(long taskId, CancellationToken cancellationToken = default);

    /// <summary>Deletes tasks in finished states together with their items. Returns the number of tasks removed.</summary>
    Task<int> DeleteFinishedAsync(CancellationToken cancellationToken = default);
}
