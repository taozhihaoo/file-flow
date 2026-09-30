namespace FileFlow.Core.Models;

/// <summary>A batch operation tracked by the task queue (row of the <c>tasks</c> table).</summary>
public class FileTask
{
    public long Id { get; set; }

    public TaskType Type { get; set; }

    public TaskState State { get; set; } = TaskState.Queued;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Number of items the task contains.</summary>
    public int Total { get; set; }

    /// <summary>Items that reached a terminal state (succeeded, failed or cancelled).</summary>
    public int Processed { get; set; }

    public int Succeeded { get; set; }

    /// <summary>Items that failed with an error.</summary>
    public int Failed { get; set; }

    /// <summary>Total number of item-level error events recorded for this task.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Human readable summary shown in the task list, e.g. "Rename with prefix 2026_".</summary>
    public string? Description { get; set; }

    /// <summary>JSON payload with per-operation details; keeps old->new path maps so Rename/Move can support Undo later.</summary>
    public string? Metadata { get; set; }
}
