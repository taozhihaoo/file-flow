namespace FileFlow.Core.Models;

/// <summary>Options controlling a directory scan.</summary>
public sealed record ScanOptions(
    bool Recursive = true,
    bool IncludeHidden = false);
