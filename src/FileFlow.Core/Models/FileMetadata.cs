namespace FileFlow.Core.Models;

/// <summary>Metadata of a file as reported by the file system abstraction.</summary>
public sealed record FileMetadata(
    string FullPath,
    string Name,
    string Extension,
    long Length,
    DateTime CreatedAtUtc,
    DateTime ModifiedAtUtc,
    bool IsHidden);
