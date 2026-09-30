namespace FileFlow.Core.Models;

/// <summary>An entry in the operation history (row of the <c>operation_history</c> table).</summary>
public class OperationRecord
{
    public long Id { get; set; }

    /// <summary>Task that produced this entry, when applicable.</summary>
    public long? TaskId { get; set; }

    /// <summary>Operation name: Rename, Move, Copy, Delete, ChangeExtension, AddTag, RemoveTag, Scan, Import.</summary>
    public required string OperationType { get; set; }

    public string? Description { get; set; }

    public int ItemCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    /// <summary>JSON payload; for Rename/Move contains the old-to-new path map (foundation for a future Undo feature).</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
