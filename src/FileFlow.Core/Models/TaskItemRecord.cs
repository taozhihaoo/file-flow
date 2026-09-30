namespace FileFlow.Core.Models;

/// <summary>One unit of work inside a <see cref="FileTask"/> (row of the <c>task_items</c> table).</summary>
public class TaskItemRecord
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public required string SourcePath { get; set; }

    /// <summary>Destination path for rename/move/copy operations; null for tag/delete operations.</summary>
    public string? TargetPath { get; set; }

    public TaskItemState State { get; set; } = TaskItemState.Pending;

    public ErrorKind ErrorKind { get; set; } = ErrorKind.None;

    public string? ErrorMessage { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }
}
