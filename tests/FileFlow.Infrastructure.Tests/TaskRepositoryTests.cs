using Xunit;
using FileFlow.Core;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.Repositories;

namespace FileFlow.Infrastructure.Tests;

public class TaskRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly TaskRepository _repository;

    public TaskRepositoryTests()
    {
        _repository = new TaskRepository(_db.Factory);
    }

    private static (FileTask Task, List<TaskItemRecord> Items) MakeTask(int itemCount = 2, TaskType type = TaskType.Rename)
    {
        var task = new FileTask
        {
            Type = type,
            State = TaskState.Queued,
            Description = "Test task",
        };
        var items = Enumerable.Range(0, itemCount).Select(i => new TaskItemRecord
        {
            SourcePath = $@"C:\src\file{i}.txt",
            TargetPath = $@"C:\dst\file{i}.txt",
        }).ToList();
        return (task, items);
    }

    [Fact]
    public async Task Create_PersistsTaskAndItems()
    {
        var (task, items) = MakeTask(3);
        var id = await _repository.CreateAsync(task, items);

        var stored = await _repository.GetByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(TaskState.Queued, stored.State);
        Assert.Equal(3, stored.Total);
        Assert.Equal(0, stored.Processed);
        Assert.True(stored.CreatedAtUtc != default);

        var storedItems = await _repository.GetItemsAsync(id);
        Assert.Equal(3, storedItems.Count);
        Assert.All(storedItems, i => Assert.Equal(TaskItemState.Pending, i.State));
    }

    [Fact]
    public async Task Update_PersistsCountersAndState()
    {
        var (task, items) = MakeTask(2);
        var id = await _repository.CreateAsync(task, items);
        var stored = (await _repository.GetByIdAsync(id))!;

        stored.State = TaskState.Running;
        stored.StartedAtUtc = DateTime.UtcNow;
        stored.Processed = 1;
        stored.Succeeded = 1;
        await _repository.UpdateAsync(stored);

        var reloaded = (await _repository.GetByIdAsync(id))!;
        Assert.Equal(TaskState.Running, reloaded.State);
        Assert.Equal(1, reloaded.Succeeded);
        Assert.NotNull(reloaded.StartedAtUtc);
    }

    [Fact]
    public async Task UpdateItem_PersistsFailureDetails()
    {
        var (task, items) = MakeTask(1);
        var id = await _repository.CreateAsync(task, items);
        var item = (await _repository.GetItemsAsync(id))[0];

        item.State = TaskItemState.Failed;
        item.ErrorKind = ErrorKind.OperationConflict;
        item.ErrorMessage = "Target exists";
        item.ProcessedAtUtc = DateTime.UtcNow;
        await _repository.UpdateItemAsync(item);

        var reloaded = (await _repository.GetItemsAsync(id))[0];
        Assert.Equal(TaskItemState.Failed, reloaded.State);
        Assert.Equal(ErrorKind.OperationConflict, reloaded.ErrorKind);
        Assert.Equal("Target exists", reloaded.ErrorMessage);
    }

    [Fact]
    public async Task GetRecent_ReturnsNewestFirst()
    {
        for (var i = 0; i < 3; i++)
        {
            var (task, items) = MakeTask(1);
            await _repository.CreateAsync(task, items);
        }

        var recent = await _repository.GetRecentAsync(2);
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task MarkRunningAsInterrupted_OnlyTouchesRunningTasks()
    {
        var (queued, queuedItems) = MakeTask();
        var queuedId = await _repository.CreateAsync(queued, queuedItems);
        var (running, runningItems) = MakeTask();
        var runningId = await _repository.CreateAsync(running, runningItems);
        var storedRunning = (await _repository.GetByIdAsync(runningId))!;
        storedRunning.State = TaskState.Running;
        await _repository.UpdateAsync(storedRunning);

        var affected = await _repository.MarkRunningAsInterruptedAsync();

        Assert.Equal(1, affected);
        Assert.Equal(TaskState.Interrupted, (await _repository.GetByIdAsync(runningId))!.State);
        Assert.Equal(TaskState.Queued, (await _repository.GetByIdAsync(queuedId))!.State);
    }

    [Fact]
    public async Task GetFailedItems_ReturnsOnlyFailed()
    {
        var (task, items) = MakeTask(3);
        var id = await _repository.CreateAsync(task, items);
        var stored = (await _repository.GetItemsAsync(id)).ToList();
        stored[0].State = TaskItemState.Succeeded;
        stored[1].State = TaskItemState.Failed;
        stored[2].State = TaskItemState.Pending;
        foreach (var item in stored)
        {
            item.ProcessedAtUtc = item.State == TaskItemState.Pending ? null : DateTime.UtcNow;
            await _repository.UpdateItemAsync(item);
        }

        var failed = await _repository.GetFailedItemsAsync(id);
        Assert.Single(failed);
        Assert.Equal(stored[1].Id, failed[0].Id);
    }

    [Fact]
    public async Task CancelPendingItems_MarksOnlyPending()
    {
        var (task, items) = MakeTask(3);
        var id = await _repository.CreateAsync(task, items);
        var stored = (await _repository.GetItemsAsync(id)).ToList();
        stored[0].State = TaskItemState.Succeeded;
        await _repository.UpdateItemAsync(stored[0]);

        var cancelled = await _repository.CancelPendingItemsAsync(id);

        Assert.Equal(2, cancelled);
        var reloaded = await _repository.GetItemsAsync(id);
        Assert.Equal(TaskItemState.Succeeded, reloaded[0].State);
        Assert.Equal(TaskItemState.Cancelled, reloaded[1].State);
        Assert.Equal(TaskItemState.Cancelled, reloaded[2].State);
    }

    [Fact]
    public async Task DeleteFinished_RemovesFinishedTasksAndItems()
    {
        var (task, items) = MakeTask(2);
        var id = await _repository.CreateAsync(task, items);
        var stored = (await _repository.GetByIdAsync(id))!;
        stored.State = TaskState.Completed;
        await _repository.UpdateAsync(stored);

        var removed = await _repository.DeleteFinishedAsync();

        Assert.Equal(1, removed);
        Assert.Null(await _repository.GetByIdAsync(id));
        Assert.Empty(await _repository.GetItemsAsync(id));
    }

    [Fact]
    public async Task DeleteFinished_KeepsActiveTasks()
    {
        var (task, items) = MakeTask();
        var id = await _repository.CreateAsync(task, items);

        await _repository.DeleteFinishedAsync();

        Assert.NotNull(await _repository.GetByIdAsync(id));
    }

    public void Dispose() => _db.Dispose();
}
