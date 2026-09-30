namespace FileFlow.Core.Models;

/// <summary>A file plus the display string of its tags, as returned by search.</summary>
public sealed record FileListItem(FileRecord File, string Tags);
