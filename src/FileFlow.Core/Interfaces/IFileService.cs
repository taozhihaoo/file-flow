namespace FileFlow.Core.Interfaces;

/// <summary>
/// Single-file file system operations with classified errors.
/// Batch orchestration (preview, queue, database updates) lives above this interface.
/// </summary>
public interface IFileService
{
    /// <summary>Renames a file within its directory or moves it. Target must not exist.</summary>
    void Rename(string sourcePath, string targetPath);

    void Copy(string sourcePath, string targetPath);

    void Delete(string path, bool toRecycleBin);
}
