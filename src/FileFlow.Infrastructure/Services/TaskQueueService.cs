using System.Text.Json;
using System.Threading.Channels;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using FileFlow.Core.State;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Services;

/// <summary>
/// Serializes task metadata as JSON. <see cref="TaskMetadata.Moves"/> stores the
/// old-to-new path map for Rename/Move operations, providing the data foundation
/// for a future Undo feature.
/// </summary>
public sealed record TaskMetadata
{
    public string? TagName { get; init; }

    public List<PathMove>? Moves { get; set; }
}

public sealed record PathMove(string From, string To);

/// <summary>
/// Background task queue. One task runs at a time; items inside a task run with bounded
/// parallelism (setting: MaxConcurrentOperations, default 4, max 16). Every item is
/// persisted as soon as it finishes, so a crash never leaves silent half-done batches -
/// the next start marks the interrupted task and the library can be reconciled by a rescan.
/// </summary>
public sealed class TaskQueueService : ITaskQueueService, IDisposable
{
    private const int ProgressEventInterval = 25;

    private readonly ITaskRepository _tasks;
    private readonly IFileRepository _files;
    private readonly ITagRepository _tags;
    private readonly IHistoryRepository _history;
    private readonly IFileService _fileService;
    private readonly ISettingsService _settings;
    private readonly ILogger<TaskQueueService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _currentTaskGate = new();
    private CancellationTokenSource? _currentTaskCts;
    private long? _currentTaskId;
    private Task _workerLoop = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    public event EventHandler<TaskEventArgs>? TaskChanged;

    public TaskQueueService(
        ITaskRepository tasks,
        IFileRepository files,
        ITagRepository tags,
        IHistoryRepository history,
        IFileService fileService,
        ISettingsService settings,
        ILoggerFactory loggerFactory)
    {
        _tasks = tasks;
        _files = files;
        _tags = tags;
        _history = history;
        _fileService = fileService;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TaskQueueService>();
    }

    public async Task<long> EnqueueAsync(
        TaskType type,
        string description,
        IReadOnlyList<TaskItemRecord> items,
        string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            throw FileFlowException.PathValidation("Cannot enqueue an empty task.");
        }

        var task = new FileTask
        {
            Type = type,
            State = TaskState.Queued,
            CreatedAtUtc = DateTime.UtcNow,
            Description = description,
            Metadata = metadata,
        };
        var taskId = await _tasks.CreateAsync(task, items, cancellationToken);
        _logger.LogInformation("Enqueued task {TaskId} ({TaskType}) with {ItemCount} items", taskId, type, items.Count);
        _channel.Writer.TryWrite(taskId);
        RaiseChanged(taskId, TaskState.Queued);
        return taskId;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_currentTaskGate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        // Crash recovery: tasks still marked Running from a previous session were interrupted.
        var interrupted = await _tasks.MarkRunningAsInterruptedAsync(cancellationToken);
        if (interrupted > 0)
        {
            _logger.LogWarning("Marked {Count} task(s) from the previous session as Interrupted", interrupted);
        }

        // Re-seed the channel with tasks that were still queued when the app closed.
        var queued = await _tasks.GetByStateAsync(TaskState.Queued, cancellationToken);
        foreach (var task in queued)
        {
            _channel.Writer.TryWrite(task.Id);
        }

        if (queued.Count > 0)
        {
            _logger.LogInformation("Resumed {Count} queued task(s) from the previous session", queued.Count);
        }

        _workerLoop = Task.Run(() => WorkerLoopAsync(_shutdownCts.Token));
    }

    public async Task CancelTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        lock (_currentTaskGate)
        {
            if (_currentTaskId == taskId && _currentTaskCts is not null)
            {
                _currentTaskCts.Cancel();
                _logger.LogInformation("Cancellation requested for running task {TaskId}", taskId);
                return;
            }
        }

        var task = await _tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null || task.State != TaskState.Queued)
        {
            return;
        }

        task.State = TaskState.Cancelled;
        task.CompletedAtUtc = DateTime.UtcNow;
        await _tasks.UpdateAsync(task, cancellationToken);
        var cancelledItems = await _tasks.CancelPendingItemsAsync(taskId, cancellationToken);
        task.Processed += cancelledItems;
        await _tasks.UpdateAsync(task, cancellationToken);
        _logger.LogInformation("Cancelled queued task {TaskId}", taskId);
        RaiseChanged(taskId, TaskState.Cancelled);
    }

    public async Task<long> RetryFailedItemsAsync(long taskId, CancellationToken cancellationToken = default)
    {
        var sourceTask = await _tasks.GetByIdAsync(taskId, cancellationToken)
                         ?? throw FileFlowException.FileNotFound($"Task {taskId} not found");
        var failedItems = await _tasks.GetFailedItemsAsync(taskId, cancellationToken);
        if (failedItems.Count == 0)
        {
            throw FileFlowException.PathValidation("This task has no failed items to retry.");
        }

        string? metadata = null;
        if (!string.IsNullOrEmpty(sourceTask.Metadata))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<TaskMetadata>(sourceTask.Metadata);
                metadata = JsonSerializer.Serialize(new TaskMetadata { TagName = parsed?.TagName });
            }
            catch (JsonException)
            {
                metadata = null;
            }
        }

        var items = failedItems.Select(item => new TaskItemRecord
        {
            SourcePath = item.SourcePath,
            TargetPath = item.TargetPath,
        }).ToList();

        return await EnqueueAsync(
            sourceTask.Type,
            $"Retry of task #{taskId} ({items.Count} item(s))",
            items,
            metadata,
            cancellationToken);
    }

    public async Task<int> ClearFinishedTasksAsync(CancellationToken cancellationToken = default)
    {
        var removed = await _tasks.DeleteFinishedAsync(cancellationToken);
        if (removed > 0)
        {
            _logger.LogInformation("Cleared {Count} finished task(s)", removed);
            RaiseChanged(0, TaskState.Completed);
        }

        return removed;
    }

    private async Task WorkerLoopAsync(CancellationToken shutdown)
    {
        try
        {
            await foreach (var taskId in _channel.Reader.ReadAllAsync(shutdown))
            {
                try
                {
                    await ExecuteTaskAsync(taskId, shutdown);
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let one broken task kill the worker loop.
                    _logger.LogError(ex, "Task {TaskId} failed with an unexpected error", taskId);
                    await MarkTaskFailedAsync(taskId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task ExecuteTaskAsync(long taskId, CancellationToken shutdown)
    {
        var task = await _tasks.GetByIdAsync(taskId, shutdown);
        if (task is null)
        {
            _logger.LogWarning("Task {TaskId} disappeared from the database; skipping", taskId);
            return;
        }

        if (task.State != TaskState.Queued)
        {
            // Cancelled while waiting in the queue.
            _logger.LogInformation("Task {TaskId} is {State}; skipping", taskId, task.State);
            return;
        }

        var items = await _tasks.GetItemsAsync(taskId, shutdown);
        var pendingItems = items.Where(i => i.State == TaskItemState.Pending).ToList();

        task.State = TaskState.Running;
        task.StartedAtUtc = DateTime.UtcNow;
        task.Total = items.Count;
        await _tasks.UpdateAsync(task, shutdown);
        RaiseChanged(taskId, TaskState.Running);
        _logger.LogInformation("Task {TaskId} started: {Description} ({Total} items)", taskId, task.Description, task.Total);

        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        lock (_currentTaskGate)
        {
            _currentTaskCts = taskCts;
            _currentTaskId = taskId;
        }

        TaskMetadata metadata;
        var cancelled = false;
        var shutdownRequested = false;
        try
        {
            metadata = await ProcessItemsAsync(task, pendingItems, taskCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal outcome; finalization below records it correctly.
            shutdownRequested = shutdown.IsCancellationRequested;
            metadata = ParseMetadata(task.Metadata) with { Moves = new List<PathMove>() };
        }
        finally
        {
            cancelled = taskCts.IsCancellationRequested;
            lock (_currentTaskGate)
            {
                _currentTaskCts = null;
                _currentTaskId = null;
            }
        }

        if (shutdownRequested)
        {
            // Application is exiting: leave the task Running so the next start
            // marks it Interrupted (crash recovery), instead of silently cancelling it.
            _logger.LogWarning("Task {TaskId} interrupted by application shutdown", taskId);
            return;
        }

        task.Metadata = JsonSerializer.Serialize(metadata);
        await FinalizeTaskAsync(task, cancelled, shutdown);
    }

    private async Task<TaskMetadata> ProcessItemsAsync(FileTask task, List<TaskItemRecord> items, CancellationToken token)
    {
        var metadata = ParseMetadata(task.Metadata) with { Moves = new List<PathMove>() };
        var executor = new TaskItemExecutor(
            _tasks,
            _files,
            _tags,
            _fileService,
            _settings,
            _loggerFactory,
            task,
            metadata,
            RaiseProgressChanged);

        var parallelism = _settings.Current.EffectiveConcurrency;
        await Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = token,
            },
            async (item, _) =>
            {
                // Contained on purpose: on cancellation ForEachAsync abandons in-flight
                // bodies, and an escaping exception here would be unobserved.
                try
                {
                    token.ThrowIfCancellationRequested();
                    await executor.ExecuteAsync(item);
                }
                catch (OperationCanceledException)
                {
                    // The item never started; FinalizeTaskAsync records it as cancelled.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Item {Source} failed outside the executor protection", item.SourcePath);
                }
            });

        return metadata;
    }

    private void RaiseProgressChanged(FileTask task)
    {
        if (task.Processed % ProgressEventInterval == 0)
        {
            RaiseChanged(task.Id, task.State);
        }
    }

    private async Task FinalizeTaskAsync(FileTask task, bool wasCancelled, CancellationToken shutdown)
    {
        // Anything still Pending never ran (cancelled midway) - give it a terminal state.
        var cancelledPending = await _tasks.CancelPendingItemsAsync(task.Id, shutdown);
        wasCancelled |= cancelledPending > 0;

        TaskState finalState;
        if (wasCancelled)
        {
            finalState = TaskState.Cancelled;
        }
        else if (task.Succeeded > 0 && task.Failed > 0)
        {
            finalState = TaskState.CompletedWithErrors;
        }
        else if (task.Succeeded == 0 && task.Failed > 0)
        {
            finalState = TaskState.Failed;
        }
        else
        {
            finalState = TaskState.Completed;
        }

        task.Processed += cancelledPending;
        TaskStateMachine.EnsureTransition(task.State, finalState);
        task.State = finalState;
        task.CompletedAtUtc = DateTime.UtcNow;
        task.ErrorCount = task.Failed;

        // The audit record is written first: a task that reads as terminal must already
        // be in the operation history, for readers that poll the database instead of
        // listening to TaskChanged (tests, dashboards, external tooling). It stays
        // best-effort so a history failure cannot change the real outcome of a task.
        try
        {
            await _history.AddAsync(
                new OperationRecord
                {
                    TaskId = task.Id,
                    OperationType = TaskTypeNames.FromTaskType(task.Type),
                    Description = task.Description,
                    ItemCount = task.Total,
                    SuccessCount = task.Succeeded,
                    FailureCount = task.Failed,
                    Metadata = task.Metadata,
                    CreatedAtUtc = DateTime.UtcNow,
                },
                shutdown);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not write the history entry of task {TaskId}", task.Id);
        }

        await _tasks.UpdateAsync(task, shutdown);

        _logger.LogInformation(
            "Task {TaskId} finished: {State} ({Succeeded}/{Total} succeeded, {Failed} failed)",
            task.Id,
            finalState,
            task.Succeeded,
            task.Total,
            task.Failed);
        RaiseChanged(task.Id, finalState);
    }

    private async Task MarkTaskFailedAsync(long taskId)
    {
        try
        {
            var task = await _tasks.GetByIdAsync(taskId);
            if (task is { State: TaskState.Running })
            {
                task.State = TaskState.Failed;
                task.CompletedAtUtc = DateTime.UtcNow;
                task.ErrorCount = task.Failed;
                await _history.AddAsync(new OperationRecord
                {
                    TaskId = task.Id,
                    OperationType = TaskTypeNames.FromTaskType(task.Type),
                    Description = task.Description,
                    ItemCount = task.Total,
                    SuccessCount = task.Succeeded,
                    FailureCount = task.Total - task.Succeeded,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                await _tasks.UpdateAsync(task);
                RaiseChanged(task.Id, TaskState.Failed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark task {TaskId} as failed", taskId);
        }
    }

    private TaskMetadata ParseMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
        {
            return new TaskMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<TaskMetadata>(metadata) ?? new TaskMetadata();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Task metadata is not valid JSON; ignoring");
            return new TaskMetadata();
        }
    }

    private void RaiseChanged(long taskId, TaskState state)
        => TaskChanged?.Invoke(this, new TaskEventArgs(taskId, state));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _channel.Writer.TryComplete();
        _shutdownCts.Cancel();
        try
        {
            _workerLoop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // worker terminated during shutdown
        }

        _shutdownCts.Dispose();
    }
}
