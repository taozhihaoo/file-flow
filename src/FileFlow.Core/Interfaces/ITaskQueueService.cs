using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

public sealed record TaskEventArgs(long TaskId, TaskState State);

/// <summary>
/// Background execution of batch operations. Long running work happens off the UI thread,
/// every item updates the database as it completes, and everything is cancellable.
/// </summary>
public interface ITaskQueueService
{
    /// <summary>Creates a queued task with its items. Returns the task id.</summary>
    Task<long> EnqueueAsync(TaskType type, string description, IReadOnlyList<TaskItemRecord> items, string? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>Runs crash recovery (marks stale Running tasks as Interrupted) and starts the worker loop.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a queued or running task.</summary>
    Task CancelTaskAsync(long taskId, CancellationToken cancellationToken = default);

    /// <summary>Re-enqueues the failed items of a task as a new task. Returns the new task id.</summary>
    Task<long> RetryFailedItemsAsync(long taskId, CancellationToken cancellationToken = default);

    /// <summary>Deletes tasks in finished states (history entries are kept).</summary>
    Task<int> ClearFinishedTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised whenever a task changes state or progress. Raised on a worker thread.</summary>
    event EventHandler<TaskEventArgs>? TaskChanged;
}
