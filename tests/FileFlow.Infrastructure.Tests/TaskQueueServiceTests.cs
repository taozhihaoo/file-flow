using Xunit;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Infrastructure;
using FileFlow.Infrastructure.FileSystem;
using FileFlow.Infrastructure.Repositories;
using FileFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

/// <summary>
/// End-to-end task queue tests: real SQLite database, real files in a temp directory.
/// </summary>
public class TaskQueueServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TestDatabase _db = new();
    private readonly TaskRepository _tasks;
    private readonly FileRepository _files;
    private readonly TagRepository _tags;
    private readonly HistoryRepository _history;
    private readonly SettingsService _settings;
    private readonly KeyedLockRegistry _locks = new();
    private readonly FileService _fileService;
    private readonly WindowsFileSystem _fileSystem = new();

    public TaskQueueServiceTests()
    {
        _tasks = new TaskRepository(_db.Factory);
        _files = new FileRepository(_db.Factory);
        _tags = new TagRepository(_db.Factory);
        _history = new HistoryRepository(_db.Factory);
        _settings = new SettingsService(new SettingsRepository(_db.Factory), NullLogger<SettingsService>.Instance);
        _fileService = new FileService(_fileSystem, _locks);
    }

    private TaskQueueService CreateQueue(int maxConcurrent = 4)
    {
        _settings.Current.MaxConcurrentOperations = maxConcurrent;
        return new TaskQueueService(
            _tasks,
            _files,
            _tags,
            _history,
            _fileService,
            _settings,
            NullLoggerFactory.Instance);
    }

    private async Task SeedLibraryAsync(params string[] relativePaths)
    {
        var records = relativePaths.Select(p => new FileRecord
        {
            FullPath = _temp.FilePath(p),
            FileName = System.IO.Path.GetFileName(p),
            Extension = System.IO.Path.GetExtension(p).ToLowerInvariant(),
            SizeBytes = 10,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            ScannedAtUtc = DateTime.UtcNow,
        }).ToList();
        await _files.UpsertAsync(records);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 10000)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromMilliseconds(timeoutMs))
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met in time.");
    }

    [Fact]
    public async Task RenameTask_RenamesFilesAndUpdatesDatabase()
    {
        _temp.WriteFile("a.txt");
        _temp.WriteFile("b.txt");
        await SeedLibraryAsync("a.txt", "b.txt");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Rename,
            "Add prefix",
            [
                new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("2026_a.txt") },
                new TaskItemRecord { SourcePath = _temp.FilePath("b.txt"), TargetPath = _temp.FilePath("2026_b.txt") },
            ]);

        await WaitForAsync(async () =>
        {
            var task = await _tasks.GetByIdAsync(taskId);
            return task is { State: TaskState.Completed };
        });

        Assert.True(System.IO.File.Exists(_temp.FilePath("2026_a.txt")));
        Assert.False(System.IO.File.Exists(_temp.FilePath("a.txt")));

        var record = await _files.GetByPathAsync(_temp.FilePath("2026_a.txt"));
        Assert.NotNull(record);
        Assert.Equal("2026_a.txt", record.FileName);
        Assert.Null(await _files.GetByPathAsync(_temp.FilePath("a.txt")));

        var history = await _history.GetByTypeAsync("Rename", 10);
        Assert.Single(history);
        Assert.Equal(2, history[0].SuccessCount);
    }

    [Fact]
    public async Task RenameTask_StoresUndoMetadata()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Rename,
            "Rename one",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("z.txt") }]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Completed });
        var task = (await _tasks.GetByIdAsync(taskId))!;

        Assert.Contains("z.txt", task.Metadata);
        Assert.Contains("a.txt", task.Metadata);
        var history = (await _history.GetByTypeAsync("Rename", 1))[0];
        Assert.NotEmpty(history.Metadata!);
    }

    [Fact]
    public async Task PartialFailure_ProducesCompletedWithErrors()
    {
        _temp.WriteFile("a.txt");
        _temp.WriteFile("b.txt");
        await SeedLibraryAsync("a.txt", "b.txt");
        System.IO.File.WriteAllText(_temp.FilePath("taken.txt"), "blocker"); // target already exists -> conflict

        using var queue = CreateQueue();
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Rename,
            "One will fail",
            [
                new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("renamed_a.txt") },
                new TaskItemRecord { SourcePath = _temp.FilePath("b.txt"), TargetPath = _temp.FilePath("taken.txt") },
            ]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.CompletedWithErrors });

        var task = (await _tasks.GetByIdAsync(taskId))!;
        Assert.Equal(1, task.Succeeded);
        Assert.Equal(1, task.Failed);
        Assert.Equal(2, task.Processed);

        var items = await _tasks.GetItemsAsync(taskId);
        Assert.Contains(items, i => i.State == TaskItemState.Failed && i.ErrorKind == ErrorKind.OperationConflict);
    }

    [Fact]
    public async Task CopyTask_InsertsLibraryRecords()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");
        _temp.SubDirectory("backup");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Copy,
            "Copy to backup",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("backup\\a.txt") }]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Completed });

        Assert.True(System.IO.File.Exists(_temp.FilePath("backup\\a.txt")));
        Assert.NotNull(await _files.GetByPathAsync(_temp.FilePath("backup\\a.txt")));
        Assert.Equal(2, await _files.CountAllAsync());
    }

    [Fact]
    public async Task DeleteTask_MarksRecordMissing()
    {
        _temp.WriteFile("doomed.txt");
        await SeedLibraryAsync("doomed.txt");
        _settings.Current.DeleteToRecycleBin = false; // permanent delete keeps tests hermetic

        using var queue = CreateQueue();
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Delete,
            "Delete one",
            [new TaskItemRecord { SourcePath = _temp.FilePath("doomed.txt") }]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Completed });

        Assert.False(System.IO.File.Exists(_temp.FilePath("doomed.txt")));
        var record = await _files.GetByPathAsync(_temp.FilePath("doomed.txt"));
        Assert.NotNull(record); // record is kept
        Assert.True(record!.IsMissing);
    }

    [Fact]
    public async Task AddTagTask_CreatesAndLinksTag()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var metadata = System.Text.Json.JsonSerializer.Serialize(new TaskMetadata { TagName = "Important" });
        var taskId = await queue.EnqueueAsync(
            TaskType.AddTag,
            "Tag one file",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt") }],
            metadata);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Completed });

        var record = (await _files.GetByPathAsync(_temp.FilePath("a.txt")))!;
        var fileTags = await _tags.GetTagsForFileAsync(record.Id);
        Assert.Single(fileTags);
        Assert.Equal("Important", fileTags[0].Name);
    }

    [Fact]
    public async Task AddTagTask_ParallelItemsCreatingSameNewTag_AllSucceed()
    {
        // Regression: five concurrent items racing to create one new tag must not
        // trip the unique constraint and fail the task.
        var names = Enumerable.Range(0, 5).Select(i => $"p\\f{i}.txt").ToArray();
        foreach (var name in names)
        {
            _temp.WriteFile(name);
        }

        await SeedLibraryAsync(names);
        _settings.Current.MaxConcurrentOperations = 4;

        using var queue = CreateQueue();
        await queue.StartAsync();
        var metadata = System.Text.Json.JsonSerializer.Serialize(new TaskMetadata { TagName = "Raced" });
        var taskId = await queue.EnqueueAsync(
            TaskType.AddTag,
            "Tag five files with a brand new tag",
            names.Select(n => new TaskItemRecord { SourcePath = _temp.FilePath(n) }).ToList(),
            metadata);

        await WaitForAsync(async () =>
        {
            var task = await _tasks.GetByIdAsync(taskId);
            return task is { State: TaskState.Completed } or { State: TaskState.Failed } or { State: TaskState.CompletedWithErrors };
        });

        var task = (await _tasks.GetByIdAsync(taskId))!;
        Assert.Equal(TaskState.Completed, task.State);
        var allTags = await _tags.GetAllAsync();
        var tag = Assert.Single(allTags, t => t.Name == "Raced");
        Assert.Equal(5, tag.FileCount);
    }

    [Fact]
    public async Task Cancel_StopsTaskAndMarksRemainingItemsCancelled()
    {
        _temp.SubDirectory("work");
        foreach (var i in Enumerable.Range(0, 40))
        {
            _temp.WriteFile($"work\\f{i}.txt");
        }

        await SeedLibraryAsync(Enumerable.Range(0, 40).Select(i => $"work\\f{i}.txt").ToArray());
        _settings.Current.DeleteToRecycleBin = false;

        // Gate every delete so items block until the test releases them. Cancellation
        // lands while items are parked in the gate, which makes the outcome deterministic.
        var gate = new System.Threading.ManualResetEventSlim(false);
        var gated = new GatedFileSystem(_fileSystem, gate);
        var gatedService = new FileService(gated, _locks);

        var queue = new TaskQueueService(
            _tasks, _files, _tags, _history, gatedService, _settings, NullLoggerFactory.Instance);
        try
        {
            await queue.StartAsync();
            var items = Enumerable.Range(0, 40).Select(i => new TaskItemRecord
            {
                SourcePath = _temp.FilePath($"work\\f{i}.txt"),
            }).ToList();
            var taskId = await queue.EnqueueAsync(TaskType.Delete, "Delete many", items);

            // Wait until the task is running and deletes are parked in the gate, then cancel.
            await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Running });
            await WaitForAsync(() => Task.FromResult(gated.ParkedCount >= 1));
            await queue.CancelTaskAsync(taskId);
            await Task.Delay(200);
            gate.Set();

            // Parked bodies finish after the release and are recorded as succeeded.
            await WaitForAsync(async () =>
            {
                var all = await _tasks.GetItemsAsync(taskId);
                return all.Any(i => i.State == TaskItemState.Succeeded);
            });
            await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Cancelled });

            var allItems = await _tasks.GetItemsAsync(taskId);
            Assert.Equal(40, allItems.Count);
            Assert.True(allItems.Count(i => i.State == TaskItemState.Succeeded) >= 1, "the in-flight item(s) should have completed");
            Assert.True(allItems.Count(i => i.State == TaskItemState.Cancelled) >= 1, "cancellation should leave some items unprocessed");
            Assert.Equal(TaskState.Cancelled, (await _tasks.GetByIdAsync(taskId))!.State);
        }
        finally
        {
            queue.Dispose();
        }
    }

    [Fact]
    public async Task RetryFailed_CreatesNewTaskThatSucceeds()
    {
        _temp.WriteFile("a.txt");
        _temp.WriteFile("b.txt");
        await SeedLibraryAsync("a.txt", "b.txt");
        System.IO.File.WriteAllText(_temp.FilePath("taken.txt"), "blocker");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var firstId = await queue.EnqueueAsync(
            TaskType.Rename,
            "With a failure",
            [
                new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("taken.txt") },
                new TaskItemRecord { SourcePath = _temp.FilePath("b.txt"), TargetPath = _temp.FilePath("renamed_b.txt") },
            ]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(firstId)) is { State: TaskState.CompletedWithErrors });

        // Remove the blocker, then retry the failed item.
        System.IO.File.Delete(_temp.FilePath("taken.txt"));
        var retryId = await queue.RetryFailedItemsAsync(firstId);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(retryId)) is { State: TaskState.Completed });

        Assert.True(System.IO.File.Exists(_temp.FilePath("taken.txt")));
        var retriedTask = (await _tasks.GetByIdAsync(retryId))!;
        Assert.Equal(1, retriedTask.Total);
        Assert.Contains("Retry", retriedTask.Description);
    }

    [Fact]
    public async Task Startup_MarksStaleRunningTasksAsInterrupted()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");
        var staleTask = new FileTask { Type = TaskType.Rename, State = TaskState.Running, Description = "stale" };
        List<TaskItemRecord> staleItems =
        [
            new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("b.txt") },
        ];
        var id = await _tasks.CreateAsync(staleTask, staleItems);

        using var queue = CreateQueue();
        await queue.StartAsync();
        await WaitForAsync(async () => (await _tasks.GetByIdAsync(id)) is { State: TaskState.Interrupted });
    }

    [Fact]
    public async Task Startup_ResumesQueuedTasks()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");
        var leftoverTask = new FileTask { Type = TaskType.Rename, State = TaskState.Queued, Description = "left over" };
        List<TaskItemRecord> leftoverItems =
        [
            new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("resumed.txt") },
        ];
        var id = await _tasks.CreateAsync(leftoverTask, leftoverItems);

        using var queue = CreateQueue();
        await queue.StartAsync();

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(id)) is { State: TaskState.Completed });
        Assert.True(System.IO.File.Exists(_temp.FilePath("resumed.txt")));
    }

    [Fact]
    public async Task CancelQueuedTask_SkipsExecution()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");

        using var queue = CreateQueue();
        // Cancel while the worker is not running yet - fully deterministic.
        var taskId = await queue.EnqueueAsync(
            TaskType.Rename,
            "Never runs",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("never.txt") }]);
        await queue.CancelTaskAsync(taskId);

        await queue.StartAsync();
        await Task.Delay(500); // give a wrongly-picked-up task time to (wrongly) run

        Assert.Equal(TaskState.Cancelled, (await _tasks.GetByIdAsync(taskId))!.State);
        Assert.False(System.IO.File.Exists(_temp.FilePath("never.txt")));
        var items = await _tasks.GetItemsAsync(taskId);
        Assert.All(items, i => Assert.Equal(TaskItemState.Cancelled, i.State));
    }

    [Fact]
    public async Task ClearFinished_RemovesFinishedButKeepsActive()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");

        using var queue = CreateQueue();
        await queue.StartAsync();
        var doneId = await queue.EnqueueAsync(
            TaskType.Rename,
            "Will finish",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("renamed.txt") }]);
        await WaitForAsync(async () => (await _tasks.GetByIdAsync(doneId)) is { State: TaskState.Completed });

        var stayTask = new FileTask { Type = TaskType.Copy, State = TaskState.Queued, Description = "stays" };
        List<TaskItemRecord> stayItems =
        [
            new TaskItemRecord { SourcePath = _temp.FilePath("renamed.txt"), TargetPath = _temp.FilePath("copy.txt") },
        ];
        var queuedId = await _tasks.CreateAsync(stayTask, stayItems);

        var removed = await queue.ClearFinishedTasksAsync();

        Assert.Equal(1, removed);
        Assert.Null(await _tasks.GetByIdAsync(doneId));
        Assert.NotNull(await _tasks.GetByIdAsync(queuedId));
    }

    [Fact]
    public async Task TaskChanged_EventsAreRaised()
    {
        _temp.WriteFile("a.txt");
        await SeedLibraryAsync("a.txt");

        using var queue = CreateQueue();
        var states = new List<TaskState>();
        queue.TaskChanged += (_, e) => { lock (states) { states.Add(e.State); } };
        await queue.StartAsync();
        var taskId = await queue.EnqueueAsync(
            TaskType.Rename,
            "Events",
            [new TaskItemRecord { SourcePath = _temp.FilePath("a.txt"), TargetPath = _temp.FilePath("b.txt") }]);

        await WaitForAsync(async () => (await _tasks.GetByIdAsync(taskId)) is { State: TaskState.Completed });

        lock (states)
        {
            Assert.Contains(TaskState.Queued, states);
            Assert.Contains(TaskState.Running, states);
            Assert.Contains(TaskState.Completed, states);
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _temp.Dispose();
    }

    /// <summary>Decorates the real file system and blocks delete operations on a gate, for deterministic cancellation tests.</summary>
    private sealed class GatedFileSystem(IFileSystem inner, System.Threading.ManualResetEventSlim gate) : IFileSystem
    {
        private int _parked;

        private int _deleteCalls;

        /// <summary>Number of delete calls currently waiting on the gate.</summary>
        public int ParkedCount => Volatile.Read(ref _parked);

        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public bool FileExists(string path) => inner.FileExists(path);

        public IEnumerable<string> EnumerateDirectoryNames(string directoryPath) => inner.EnumerateDirectoryNames(directoryPath);

        public IEnumerable<string> EnumerateFileNames(string directoryPath) => inner.EnumerateFileNames(directoryPath);

        public FileMetadata GetFileMetadata(string path) => inner.GetFileMetadata(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void MoveFile(string sourcePath, string targetPath) => inner.MoveFile(sourcePath, targetPath);

        public void CopyFile(string sourcePath, string targetPath) => inner.CopyFile(sourcePath, targetPath);

        public void DeleteFile(string path, bool toRecycleBin)
        {
            Interlocked.Increment(ref _deleteCalls);
            Interlocked.Increment(ref _parked);
            try
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                inner.DeleteFile(path, toRecycleBin);
            }
            finally
            {
                Interlocked.Decrement(ref _parked);
            }
        }
    }
}
