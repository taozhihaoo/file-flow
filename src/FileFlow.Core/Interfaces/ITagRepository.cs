using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>Persistence for tags and file-tag relations.</summary>
public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a tag; names are unique (case-insensitive).</summary>
    Task<Tag> CreateAsync(string name, string? color = null, CancellationToken cancellationToken = default);

    Task RenameAsync(long tagId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tag and (via cascade) all its file relations.</summary>
    Task DeleteAsync(long tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetTagsForFileAsync(long fileId, CancellationToken cancellationToken = default);

    Task AddTagToFilesAsync(long tagId, IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default);

    Task RemoveTagFromFilesAsync(long tagId, IReadOnlyCollection<long> fileIds, CancellationToken cancellationToken = default);
}
