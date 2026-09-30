using System.Globalization;
using System.Text;
using FileFlow.Core;
using FileFlow.Core.Csv;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using FileFlow.Core.Validation;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Services;

/// <summary>CSV export and import of file metadata. File contents are never read or written.</summary>
public sealed class ImportExportService : IImportExportService
{
    public static readonly IReadOnlyList<string> RequiredColumns = ["file_name", "full_path", "size_bytes", "modified_at"];

    private static readonly IReadOnlyList<string> ExportColumns = ["file_name", "full_path", "extension", "size_bytes", "modified_at", "tags"];

    private const string DateTimeFormat = "O";
    private const int DbBatchSize = 100;

    private readonly IFileRepository _files;
    private readonly ITagRepository _tags;
    private readonly IHistoryRepository _history;
    private readonly ILogger<ImportExportService> _logger;

    public ImportExportService(IFileRepository files, ITagRepository tags, IHistoryRepository history, ILogger<ImportExportService> logger)
    {
        _files = files;
        _tags = tags;
        _history = history;
        _logger = logger;
    }

    public async Task<ExportResult> ExportAsync(string filePath, SearchFilter? filter, CancellationToken cancellationToken = default)
    {
        var pathError = PathValidator.ValidateFullPath(filePath);
        if (pathError is not null)
        {
            throw FileFlowException.PathValidation($"Invalid export path: {pathError}");
        }

        var effectiveFilter = filter ?? new SearchFilter();

        var written = 0;
        await Task.Run(
            async () =>
            {
                var directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                // UTF-8 with BOM so Excel opens the file with the right encoding.
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                await writer.WriteLineAsync(CsvWriter.WriteRow(ExportColumns));
                var pageOffset = 0;
                const int pageSize = 1000;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (items, _) = await _files.SearchAsync(Page(effectiveFilter, pageOffset, pageSize), cancellationToken);
                    if (items.Count == 0)
                    {
                        break;
                    }

                    foreach (var item in items)
                    {
                        var row = CsvWriter.WriteRow(
                        [
                            item.File.FileName,
                            item.File.FullPath,
                            item.File.Extension,
                            item.File.SizeBytes.ToString(CultureInfo.InvariantCulture),
                            item.File.ModifiedAtUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                            item.Tags,
                        ]);
                        await writer.WriteLineAsync(row);
                        written++;
                    }

                    pageOffset += items.Count;
                    if (items.Count < pageSize)
                    {
                        break;
                    }
                }
            },
            cancellationToken);

        _logger.LogInformation("Exported {Count} file record(s) to CSV", written);
        await _history.AddAsync(
            new OperationRecord
            {
                OperationType = TaskTypeNames.Export,
                Description = $"Exported to {filePath}",
                ItemCount = written,
                SuccessCount = written,
                FailureCount = 0,
                CreatedAtUtc = DateTime.UtcNow,
            });
        return new ExportResult(written, filePath);
    }

    public async Task<ImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var pathError = PathValidator.ValidateFullPath(filePath);
        if (pathError is not null)
        {
            throw FileFlowException.PathValidation($"Invalid import path: {pathError}");
        }

        if (!File.Exists(filePath))
        {
            throw FileFlowException.FileNotFound(filePath);
        }

        var result = new ImportResult();
        List<(string Path, FileRecord Record, List<string> Tags)> pending;
        try
        {
            pending = await Task.Run(() => ParseRows(filePath, result), cancellationToken);
        }
        catch (Exception ex)
        {
            throw FileFlowException.Persistence($"Could not read the CSV file: {ex.Message}", ex);
        }

        // Deduplicate against the library and insert/update in batches.
        var seenPaths = new HashSet<string>(pending.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
        var batch = new List<FileRecord>(DbBatchSize);
        foreach (var entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await _files.GetByPathAsync(entry.Path, cancellationToken);
            if (existing is null)
            {
                result.Imported++;
                batch.Add(entry.Record);
            }
            else
            {
                existing.SizeBytes = entry.Record.SizeBytes;
                existing.ModifiedAtUtc = entry.Record.ModifiedAtUtc;
                existing.CreatedAtUtc = entry.Record.CreatedAtUtc;
                existing.IsMissing = false;
                existing.ScannedAtUtc = DateTime.UtcNow;
                result.Updated++;
                batch.Add(existing);
            }

            if (batch.Count >= DbBatchSize)
            {
                await _files.UpsertAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _files.UpsertAsync(batch, cancellationToken);
        }

        // Link tags (create-if-missing) after all records exist in the library.
        foreach (var entry in pending)
        {
            if (entry.Tags.Count == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var record = await _files.GetByPathAsync(entry.Path, cancellationToken);
            if (record is null)
            {
                continue;
            }

            foreach (var tagName in entry.Tags)
            {
                var tag = await _tags.GetByNameAsync(tagName, cancellationToken) ?? await _tags.CreateAsync(tagName, null, cancellationToken);
                await _tags.AddTagToFilesAsync(tag.Id, [record.Id], cancellationToken);
            }
        }

        _logger.LogInformation(
            "CSV import finished: {Imported} imported, {Updated} updated, {Duplicates} duplicate rows, {Errors} invalid rows",
            result.Imported,
            result.Updated,
            result.SkippedDuplicates,
            result.Errors.Count);
        await _history.AddAsync(
            new OperationRecord
            {
                OperationType = TaskTypeNames.Import,
                Description = $"Imported from {filePath}",
                ItemCount = result.RowsRead,
                SuccessCount = result.Imported + result.Updated,
                FailureCount = result.Errors.Count,
                CreatedAtUtc = DateTime.UtcNow,
            });
        return result;
    }

    private static SearchFilter Page(SearchFilter source, int offset, int limit) => new()
    {
        Keyword = source.Keyword,
        Extension = source.Extension,
        MinSizeBytes = source.MinSizeBytes,
        MaxSizeBytes = source.MaxSizeBytes,
        ModifiedFromUtc = source.ModifiedFromUtc,
        ModifiedToUtc = source.ModifiedToUtc,
        TagIds = source.TagIds,
        Missing = source.Missing,
        RootPath = source.RootPath,
        Offset = offset,
        Limit = limit,
    };

    private List<(string Path, FileRecord Record, List<string> Tags)> ParseRows(string filePath, ImportResult result)
    {
        var pending = new List<(string, FileRecord, List<string>)>();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var parsed = CsvParser.Parse(reader);

        foreach (var error in parsed.Errors)
        {
            result.AddError(0, error);
        }

        if (parsed.Rows.Count == 0)
        {
            result.AddError(1, "The file is empty.");
            return pending;
        }

        var headers = parsed.Rows[0].Fields
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();
        foreach (var required in RequiredColumns)
        {
            if (!headers.Contains(required))
            {
                throw FileFlowException.PathValidation(
                    $"Invalid CSV header: column '{required}' is missing. Required columns: {string.Join(", ", RequiredColumns)}.");
            }
        }

        var columnMap = new Dictionary<string, int>();
        for (var i = 0; i < headers.Length; i++)
        {
            columnMap[headers[i]] = i;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rowNumber, fields) in parsed.Rows.Skip(1))
        {
            result.RowsRead++;

            string GetField(string name)
                => columnMap.TryGetValue(name, out var index) && index < fields.Length
                    ? fields[index].Trim()
                    : string.Empty;

            var fullPath = GetField("full_path");
            var fileName = GetField("file_name");
            var extension = GetField("extension");
            var sizeText = GetField("size_bytes");
            var modifiedText = GetField("modified_at");
            var tagsText = GetField("tags");

            if (fullPath.Length == 0)
            {
                result.AddError(rowNumber, "full_path is empty.");
                continue;
            }

            var pathError = PathValidator.ValidateFullPath(fullPath);
            if (pathError is not null)
            {
                result.AddError(rowNumber, $"Invalid path: {pathError}");
                continue;
            }

            if (fileName.Length == 0)
            {
                fileName = System.IO.Path.GetFileName(fullPath);
            }

            if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size < 0)
            {
                result.AddError(rowNumber, $"'{sizeText}' is not a valid file size.");
                continue;
            }

            DateTime modifiedUtc;
            try
            {
                modifiedUtc = DateTimeOffset.Parse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;
            }
            catch (FormatException)
            {
                result.AddError(rowNumber, $"'{modifiedText}' is not a valid date.");
                continue;
            }

            if (!seenPaths.Add(fullPath))
            {
                result.SkippedDuplicates++;
                continue;
            }

            var tags = tagsText
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            pending.Add((
                System.IO.Path.GetFullPath(fullPath),
                new FileRecord
                {
                    FullPath = System.IO.Path.GetFullPath(fullPath),
                    FileName = fileName,
                    Extension = extension.StartsWith('.') ? extension.ToLowerInvariant() : string.Empty,
                    SizeBytes = size,
                    CreatedAtUtc = modifiedUtc,
                    ModifiedAtUtc = modifiedUtc,
                    ScannedAtUtc = DateTime.UtcNow,
                },
                tags));
        }

        return pending;
    }
}
