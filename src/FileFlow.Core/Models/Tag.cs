namespace FileFlow.Core.Models;

/// <summary>A user-defined label that can be attached to files.</summary>
public class Tag
{
    public long Id { get; set; }

    /// <summary>Unique, case-insensitive tag name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional hex color for UI display, e.g. "#2563EB".</summary>
    public string? Color { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Number of indexed files carrying this tag (populated by queries).</summary>
    public int FileCount { get; set; }
}
