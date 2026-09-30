using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Keeps the file library (database) in sync with the disk.</summary>
public interface ILibraryService
{
    /// <summary>
    /// Scans a directory and upserts all found files. Files under the root that were not
    /// seen during the scan are marked as missing. The scan itself runs on a worker thread.
    /// </summary>
    Task<ScanResult> ScanDirectoryAsync(string rootPath, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Re-checks the given records against the disk and updates their missing flag.</summary>
    Task<int> RefreshMissingAsync(IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default);
}
