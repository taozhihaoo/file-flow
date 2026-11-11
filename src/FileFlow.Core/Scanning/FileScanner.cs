using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Scanning;

/// <summary>
/// Enumerates a directory tree and produces <see cref="FileRecord"/> candidates.
/// Depends only on <see cref="IFileSystem"/> so it can be tested against an in-memory file system.
/// A failure on a single file or directory is recorded as a <see cref="ScanError"/> and never aborts the scan.
/// </summary>
public sealed class FileScanner
{
    private const int ProgressBatchSize = 50;

    /// <summary>Minimum time between progress callbacks; the UI dispatches one message per report.</summary>
    private const int ProgressIntervalMs = 120;

    private readonly IFileSystem _fileSystem;

    public FileScanner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>Blocking scan; callers run it on a background thread. Throws OperationCanceledException on cancellation.</summary>
    public ScanResult Scan(string rootPath, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var rootError = PathValidator.ValidateDirectoryPath(rootPath);
        if (rootError is not null)
        {
            throw FileFlowException.PathValidation($"Cannot scan: {rootError}");
        }

        if (!_fileSystem.DirectoryExists(rootPath))
        {
            throw FileFlowException.PathValidation($"Directory does not exist: {rootPath}");
        }

        var root = System.IO.Path.GetFullPath(rootPath);
        var result = new ScanResult { RootPath = root };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pending = new Stack<string>();
        pending.Push(root);
        var filesFound = 0;

        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                result.DirectoriesVisited++;

                var childDirectories = SafeEnumerate(
                    () => _fileSystem.EnumerateDirectoryNames(directory),
                    directory,
                    "directory",
                    result);
                if (childDirectories is null)
                {
                    continue;
                }

                if (options.Recursive)
                {
                    foreach (var name in childDirectories)
                    {
                        pending.Push(System.IO.Path.Combine(directory, name));
                    }
                }

                var fileNames = SafeEnumerate(
                    () => _fileSystem.EnumerateFileNames(directory),
                    directory,
                    "directory",
                    result);
                if (fileNames is null)
                {
                    continue;
                }

                foreach (var fileName in fileNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath = System.IO.Path.Combine(directory, fileName);
                    FileMetadata metadata;
                    try
                    {
                        metadata = _fileSystem.GetFileMetadata(fullPath);
                    }
                    catch (Exception ex)
                    {
                        result.AddError(new ScanError(fullPath, ex.Message));
                        continue;
                    }

                    if (metadata.IsHidden && !options.IncludeHidden)
                    {
                        continue;
                    }

                    var scannedAt = DateTime.UtcNow;
                    result.AddFile(new FileRecord
                    {
                        FullPath = System.IO.Path.GetFullPath(fullPath),
                        FileName = metadata.Name,
                        Extension = NormalizeExtension(metadata.Extension),
                        SizeBytes = metadata.Length,
                        CreatedAtUtc = metadata.CreatedAtUtc,
                        ModifiedAtUtc = metadata.ModifiedAtUtc,
                        ScannedAtUtc = scannedAt,
                    });
                    filesFound++;

                    if (filesFound % ProgressBatchSize == 0 && progressStopwatch.ElapsedMilliseconds >= ProgressIntervalMs)
                    {
                        Report(progress, result, filesFound, directory);
                        progressStopwatch.Restart();
                    }
                }
            }

            // A final report so a small or very fast scan still ends with the real total.
            Report(progress, result, filesFound, root);
        }
        finally
        {
            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;
        }

        return result;
    }

    private static IEnumerable<string>? SafeEnumerate(
        Func<IEnumerable<string>> enumerate,
        string path,
        string kind,
        ScanResult result)
    {
        try
        {
            // Materialize now so failures during lazy enumeration are attributed correctly.
            return enumerate().ToArray();
        }
        catch (Exception ex)
        {
            result.AddError(new ScanError(path, $"Could not enumerate {kind}: {ex.Message}"));
            return null;
        }
    }

    private static void Report(IProgress<ScanProgress>? progress, ScanResult result, int filesFound, string currentPath)
        => progress?.Report(new ScanProgress(filesFound, result.DirectoriesVisited, result.Errors.Count, currentPath));

    public static string NormalizeExtension(string extension)
        => string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
}
