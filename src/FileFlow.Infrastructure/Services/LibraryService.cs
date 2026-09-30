using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using FileFlow.Core.Scanning;
using FileFlow.Core.Validation;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Services;

/// <summary>Keeps the file library in sync with the disk: scans, upserts and missing-file handling.</summary>
public sealed class LibraryService : ILibraryService
{
    private const int UpsertChunkSize = 500;

    private readonly FileScanner _scanner;
    private readonly IFileRepository _files;
    private readonly IHistoryRepository _history;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        FileScanner scanner,
        IFileRepository files,
        IHistoryRepository history,
        IFileSystem fileSystem,
        ILogger<LibraryService> logger)
    {
        _scanner = scanner;
        _files = files;
        _history = history;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<ScanResult> ScanDirectoryAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            async () =>
            {
                var result = _scanner.Scan(rootPath, options, progress, cancellationToken);
                _logger.LogInformation(
                    "Scan of {Root} finished: {Files} files, {Errors} errors, {Elapsed}",
                    result.RootPath,
                    result.Files.Count,
                    result.Errors.Count,
                    result.Elapsed);

                for (var offset = 0; offset < result.Files.Count; offset += UpsertChunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = result.Files.Skip(offset).Take(UpsertChunkSize).ToList();
                    await _files.UpsertAsync(chunk, cancellationToken);
                }

                // Files under this root that the scan did not see are considered missing.
                var seenPaths = new HashSet<string>(
                    result.Files.Select(f => f.FullPath),
                    StringComparer.OrdinalIgnoreCase);
                var markedMissing = await _files.MarkMissingExceptAsync(result.RootPath, seenPaths, cancellationToken);
                if (markedMissing > 0)
                {
                    _logger.LogInformation("Scan marked {Count} file(s) under {Root} as missing", markedMissing, result.RootPath);
                }

                await _history.AddAsync(
                    new OperationRecord
                    {
                        OperationType = TaskTypeNames.Scan,
                        Description = $"Scanned {result.RootPath}",
                        ItemCount = result.Files.Count,
                        SuccessCount = result.Files.Count,
                        FailureCount = result.Errors.Count,
                        CreatedAtUtc = DateTime.UtcNow,
                    },
                    cancellationToken);

                return result;
            },
            cancellationToken);
    }

    public async Task<int> RefreshMissingAsync(IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default)
    {
        if (fileIds.Count == 0)
        {
            return 0;
        }

        return await Task.Run(
            async () =>
            {
                var records = await _files.GetByIdsAsync(fileIds, cancellationToken);
                var changed = 0;
                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var exists = _fileSystem.FileExists(record.FullPath);
                    if (exists == record.IsMissing)
                    {
                        await _files.SetMissingAsync(record.Id, !exists, cancellationToken);
                        changed++;
                    }
                }

                return changed;
            },
            cancellationToken);
    }
}
