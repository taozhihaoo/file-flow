using FileFlow.Core.Interfaces;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Planning;

/// <summary>User editable rename rules applied to a selection of files.</summary>
public sealed class RenameRules
{
    public string Prefix { get; set; } = string.Empty;

    public string Suffix { get; set; } = string.Empty;

    /// <summary>Text to look for inside the base name. Empty disables replacement.</summary>
    public string FindText { get; set; } = string.Empty;

    /// <summary>Replacement for <see cref="FindText"/>. Case-insensitive match.</summary>
    public string ReplaceText { get; set; } = string.Empty;

    /// <summary>When true, appends a sequential number to every base name.</summary>
    public bool UseSequentialNumbers { get; set; }

    public int SequentialStart { get; set; } = 1;

    /// <summary>Minimum digit count, zero padded. 3 produces file_001.pdf.</summary>
    public int SequentialPadding { get; set; } = 3;

    public string SequentialSeparator { get; set; } = "_";

    /// <summary>True when no rule would change any name.</summary>
    public bool IsNoOp()
        => string.IsNullOrEmpty(Prefix)
           && string.IsNullOrEmpty(Suffix)
           && string.IsNullOrEmpty(FindText)
           && !UseSequentialNumbers;
}

/// <summary>A file selected for a rename-style operation.</summary>
public sealed record RenameInput(long FileId, string FullPath);

/// <summary>Planned outcome for one file.</summary>
public sealed class PlanEntry
{
    public long FileId { get; init; }

    public required string SourcePath { get; init; }

    public required string CurrentName { get; init; }

    public required string NewName { get; init; }

    public required string TargetPath { get; init; }

    public ErrorKind ErrorKind { get; init; } = ErrorKind.None;

    public string? Error { get; init; }

    public bool IsValid => Error is null;
}

/// <summary>Complete preview of a rename-style operation.</summary>
public sealed class RenamePlan
{
    public required string OperationName { get; init; }

    public required IReadOnlyList<PlanEntry> Entries { get; init; }

    public int ValidCount => Entries.Count(e => e.IsValid);

    public int ErrorCount => Entries.Count - ValidCount;
}
