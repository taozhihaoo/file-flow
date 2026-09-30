namespace FileFlow.Core.Models;

/// <summary>Progress snapshot reported while a scan is running.</summary>
public sealed record ScanProgress(
    int FilesFound,
    int DirectoriesVisited,
    int ErrorCount,
    string CurrentPath);

/// <summary>A non-fatal problem encountered during a scan (one unreadable directory or file).</summary>
public sealed record ScanError(string Path, string Message);

/// <summary>Outcome of a completed scan.</summary>
public sealed class ScanResult
{
    private readonly List<FileRecord> _files = new();
    private readonly List<ScanError> _errors = new();

    public string RootPath { get; init; } = string.Empty;

    public int DirectoriesVisited { get; set; }

    public TimeSpan Elapsed { get; set; }

    public IReadOnlyList<FileRecord> Files => _files;

    public IReadOnlyList<ScanError> Errors => _errors;

    internal void AddFile(FileRecord record) => _files.Add(record);

    internal void AddError(ScanError error) => _errors.Add(error);
}
