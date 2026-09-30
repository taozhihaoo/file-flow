using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Outcome of a CSV export.</summary>
public sealed record ExportResult(int FileCount, string FilePath);

/// <summary>Outcome of a CSV import.</summary>
public sealed class ImportResult
{
    public int RowsRead { get; set; }

    /// <summary>Rows that created a new library record.</summary>
    public int Imported { get; set; }

    /// <summary>Rows whose full_path already existed (record refreshed, no duplicate created).</summary>
    public int Updated { get; set; }

    /// <summary>Rows skipped because they duplicate an earlier row in the same file.</summary>
    public int SkippedDuplicates { get; set; }

    /// <summary>Invalid rows with their 1-based row number and the reason.</summary>
    public IReadOnlyList<(int Row, string Reason)> Errors => _errors;

    private readonly List<(int Row, string Reason)> _errors = new();

    /// <summary>Records an invalid row with its 1-based row number and the reason.</summary>
    public void AddError(int row, string reason) => _errors.Add((row, reason));
}

/// <summary>CSV import/export of file metadata (never file contents).</summary>
public interface IImportExportService
{
    /// <summary>Exports files matching the filter. Headers: file_name, full_path, extension, size_bytes, modified_at, tags.</summary>
    Task<ExportResult> ExportAsync(string filePath, SearchFilter? filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports metadata rows. Validates headers, skips invalid rows (reporting the row number)
    /// and avoids duplicates both inside the file and against the library.
    /// </summary>
    Task<ImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
