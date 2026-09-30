namespace FileFlow.Core.Models;

/// <summary>A file indexed by FileFlow. One row of the <c>files</c> table.</summary>
public class FileRecord
{
    public long Id { get; set; }

    /// <summary>Absolute, normalized path. Unique within the library.</summary>
    public required string FullPath { get; set; }

    public required string FileName { get; set; }

    /// <summary>Lowercase extension including the leading dot; empty for files without extension.</summary>
    public string Extension { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ModifiedAtUtc { get; set; }

    public DateTime ScannedAtUtc { get; set; }

    /// <summary>Optional content hash; only computed on demand, never during a normal scan.</summary>
    public string? ContentHash { get; set; }

    /// <summary>True when the file existed in the library but is no longer present on disk.</summary>
    public bool IsMissing { get; set; }
}
