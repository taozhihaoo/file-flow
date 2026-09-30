namespace FileFlow.Core;

/// <summary>Lifecycle states of a queued task.</summary>
public enum TaskState
{
    /// <summary>Waiting in the queue, not started yet.</summary>
    Queued,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Finished, every item succeeded.</summary>
    Completed,

    /// <summary>Finished, at least one item succeeded and at least one item failed.</summary>
    CompletedWithErrors,

    /// <summary>Finished without any item succeeding.</summary>
    Failed,

    /// <summary>Cancelled by the user before completion.</summary>
    Cancelled,

    /// <summary>The application exited while the task was queued or running; detected on next start.</summary>
    Interrupted,
}

/// <summary>Kinds of batch operations the task queue can execute.</summary>
public enum TaskType
{
    Rename,
    Move,
    Copy,
    Delete,
    ChangeExtension,
    AddTag,
    RemoveTag,
}

/// <summary>Execution state of a single item inside a task.</summary>
public enum TaskItemState
{
    Pending,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>Classification of errors raised by file operations and persistence.</summary>
public enum ErrorKind
{
    None,

    /// <summary>Access denied, locked file, disk full, media error.</summary>
    FileAccess,

    /// <summary>Source file or directory no longer exists.</summary>
    FileNotFound,

    /// <summary>Empty, malformed, too long or otherwise unusable path/filename.</summary>
    PathValidation,

    /// <summary>Target already exists or another operation conflicts with this one.</summary>
    OperationConflict,

    /// <summary>Database read/write failed.</summary>
    Persistence,

    /// <summary>The operation was cancelled through its cancellation token.</summary>
    Cancelled,

    /// <summary>Anything not covered by the categories above.</summary>
    Unknown,
}
