using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Persistence for indexed files. Implementations must keep full_path unique.</summary>
public interface IFileRepository
{
    /// <summary>Inserts new records and updates existing ones (matched by full_path) in one transaction.</summary>
    Task UpsertAsync(IReadOnlyCollection<FileRecord> records, CancellationToken cancellationToken = default);

    Task<FileRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<FileRecord?> GetByPathAsync(string fullPath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileRecord>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Updates the location fields after a rename or move.</summary>
    Task UpdatePathAsync(long id, string newFullPath, string newFileName, string newExtension, CancellationToken cancellationToken = default);

    /// <summary>Paged search. Returns the matching page and the total match count.</summary>
    Task<(IReadOnlyList<FileListItem> Items, int TotalCount)> SearchAsync(SearchFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetExtensionsAsync(CancellationToken cancellationToken = default);

    Task<int> CountAllAsync(CancellationToken cancellationToken = default);

    Task<long> SumSizeAsync(CancellationToken cancellationToken = default);

    Task<int> CountMissingAsync(CancellationToken cancellationToken = default);

    Task SetMissingAsync(long id, bool missing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every indexed file under <paramref name="rootPath"/> that is not in
    /// <paramref name="seenPaths"/> as missing. Returns the number of records updated.
    /// </summary>
    Task<int> MarkMissingExceptAsync(string rootPath, IReadOnlySet<string> seenPaths, CancellationToken cancellationToken = default);
}
