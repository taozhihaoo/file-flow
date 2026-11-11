using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Services;

/// <summary>
/// Executes one task item and keeps the task counters and the database in sync.
/// A failure on a single item never aborts the batch; it is recorded with its
/// classified error kind and the remaining items continue.
/// </summary>
internal sealed class TaskItemExecutor
{
    private readonly ITaskRepository _tasks;
    private readonly IFileRepository _files;
    private readonly ITagRepository _tags;
    private readonly IFileService _fileService;
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly FileTask _task;
    private readonly TaskMetadata _metadata;
    private readonly Action<FileTask> _progressChanged;

    /// <summary>Serializes the counter update + its database write so concurrent items cannot overwrite each other.</summary>
    private readonly SemaphoreSlim _counterGate = new(1, 1);

    public TaskItemExecutor(
        ITaskRepository tasks,
        IFileRepository files,
        ITagRepository tags,
        IFileService fileService,
        ISettingsService settings,
        ILoggerFactory loggerFactory,
        FileTask task,
        TaskMetadata metadata,
        Action<FileTask> progressChanged)
    {
        _tasks = tasks;
        _files = files;
        _tags = tags;
        _fileService = fileService;
        _settings = settings;
        _task = task;
        _metadata = metadata;
        _progressChanged = progressChanged;
        _logger = loggerFactory.CreateLogger<TaskItemExecutor>();
    }

    public async Task ExecuteAsync(TaskItemRecord item)
    {
        try
        {
            switch (_task.Type)
            {
                case TaskType.Rename:
                case TaskType.Move:
                case TaskType.ChangeExtension:
                    await ExecutePathChangeAsync(item);
                    break;
                case TaskType.Copy:
                    await ExecuteCopyAsync(item);
                    break;
                case TaskType.Delete:
                    await ExecuteDeleteAsync(item);
                    break;
                case TaskType.AddTag:
                case TaskType.RemoveTag:
                    await ExecuteTagAsync(item);
                    break;
                default:
                    throw FileFlowException.PathValidation($"Unsupported task type {_task.Type}.");
            }

            MarkItem(item, TaskItemState.Succeeded, ErrorKind.None, null);
        }
        catch (FileFlowException ex)
        {
            _logger.LogWarning("Task {TaskId} item failed ({Kind}): {Message}", _task.Id, ex.Kind, ex.Message);
            MarkItem(item, TaskItemState.Failed, ex.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task {TaskId} item failed unexpectedly: {Source}", _task.Id, item.SourcePath);
            MarkItem(item, TaskItemState.Failed, ErrorKind.Unknown, ex.Message);
        }

        await PersistItemAsync(item);
        await PersistCountersAsync(item);
        _progressChanged(_task);
    }

    private async Task ExecutePathChangeAsync(TaskItemRecord item)
    {
        if (string.IsNullOrEmpty(item.TargetPath))
        {
            throw FileFlowException.PathValidation("Task item has no target path.");
        }

        _fileService.Rename(item.SourcePath, item.TargetPath);

        lock (_metadata)
        {
            _metadata.Moves ??= new List<PathMove>();
            _metadata.Moves.Add(new PathMove(item.SourcePath, item.TargetPath));
        }

        await UpdateLibraryPathAsync(item.SourcePath, item.TargetPath);
    }

    private async Task ExecuteCopyAsync(TaskItemRecord item)
    {
        if (string.IsNullOrEmpty(item.TargetPath))
        {
            throw FileFlowException.PathValidation("Task item has no target path.");
        }

        _fileService.Copy(item.SourcePath, item.TargetPath);

        try
        {
            var record = await _files.GetByPathAsync(item.SourcePath);
            if (record is null)
            {
                _logger.LogWarning("Copied file was not indexed; library unchanged: {Path}", item.SourcePath);
                return;
            }

            var targetName = System.IO.Path.GetFileName(item.TargetPath);
            await _files.UpsertAsync(
            [
                new FileRecord
                {
                    FullPath = item.TargetPath,
                    FileName = targetName,
                    Extension = FileScanner.NormalizeExtension(System.IO.Path.GetExtension(item.TargetPath)),
                    SizeBytes = record.SizeBytes,
                    CreatedAtUtc = record.CreatedAtUtc,
                    ModifiedAtUtc = record.ModifiedAtUtc,
                    ScannedAtUtc = DateTime.UtcNow,
                },
            ]);
        }
        catch (Exception ex)
        {
            throw FileFlowException.Persistence(
                "File was copied on disk, but the library could not be updated. Run a rescan to reconcile.", ex);
        }
    }

    private async Task ExecuteDeleteAsync(TaskItemRecord item)
    {
        var toRecycleBin = _settings.Current.DeleteToRecycleBin;
        _fileService.Delete(item.SourcePath, toRecycleBin);

        try
        {
            var record = await _files.GetByPathAsync(item.SourcePath);
            if (record is not null)
            {
                // Deleted files keep their library record with is_missing = 1.
                await _files.SetMissingAsync(record.Id, true);
            }
        }
        catch (Exception ex)
        {
            throw FileFlowException.Persistence(
                "File was deleted on disk, but the library could not be updated. Run a rescan to reconcile.", ex);
        }
    }

    private async Task ExecuteTagAsync(TaskItemRecord item)
    {
        var tagName = _metadata.TagName;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw FileFlowException.PathValidation("No tag name was provided for this task.");
        }

        var record = await _files.GetByPathAsync(item.SourcePath);
        if (record is null)
        {
            throw FileFlowException.FileNotFound($"File is not in the library: {item.SourcePath}");
        }

        if (_task.Type == TaskType.AddTag)
        {
            // Parallel items may race to create the same new tag; the unique constraint
            // makes one creator win, the losers re-fetch.
            var tag = await _tags.GetByNameAsync(tagName);
            if (tag is null)
            {
                try
                {
                    tag = await _tags.CreateAsync(tagName);
                }
                catch (FileFlowException ex) when (ex.Kind == ErrorKind.OperationConflict)
                {
                    tag = await _tags.GetByNameAsync(tagName)
                          ?? throw FileFlowException.Persistence($"Tag '{tagName}' could not be created.", ex);
                }
            }

            await _tags.AddTagToFilesAsync(tag.Id, [record.Id]);
        }
        else
        {
            var tag = await _tags.GetByNameAsync(tagName);
            if (tag is not null)
            {
                await _tags.RemoveTagFromFilesAsync(tag.Id, [record.Id]);
            }
        }
    }

    /// <summary>File system change succeeded; update the library row to match the new path.</summary>
    private async Task UpdateLibraryPathAsync(string sourcePath, string targetPath)
    {
        try
        {
            var record = await _files.GetByPathAsync(sourcePath);
            if (record is null)
            {
                _logger.LogWarning("Renamed/moved file was not indexed; library unchanged: {Path}", sourcePath);
                return;
            }

            var newName = System.IO.Path.GetFileName(targetPath);
            var newExtension = FileScanner.NormalizeExtension(System.IO.Path.GetExtension(targetPath));
            await _files.UpdatePathAsync(record.Id, targetPath, newName, newExtension);
        }
        catch (Exception ex)
        {
            throw FileFlowException.Persistence(
                "File was renamed on disk, but the library could not be updated. Run a rescan to reconcile.", ex);
        }
    }

    private void MarkItem(TaskItemRecord item, TaskItemState state, ErrorKind kind, string? message)
    {
        item.State = state;
        item.ErrorKind = kind;
        item.ErrorMessage = message;
        item.ProcessedAtUtc = DateTime.UtcNow;
    }

    private async Task PersistItemAsync(TaskItemRecord item)
    {
        try
        {
            await _tasks.UpdateItemAsync(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist state of item {ItemId}", item.Id);
        }
    }

    private async Task PersistCountersAsync(TaskItemRecord item)
    {
        await _counterGate.WaitAsync();
        try
        {
            _task.Processed++;
            switch (item.State)
            {
                case TaskItemState.Succeeded:
                    _task.Succeeded++;
                    break;
                case TaskItemState.Failed:
                    _task.Failed++;
                    _task.ErrorCount = _task.Failed;
                    break;
            }

            await _tasks.UpdateAsync(_task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist the progress of task {TaskId}", _task.Id);
        }
        finally
        {
            _counterGate.Release();
        }
    }
}
