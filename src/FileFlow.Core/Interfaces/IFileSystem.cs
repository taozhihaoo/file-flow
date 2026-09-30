using FileFlow.Core.Models;

namespace FileFlow.Core.Interfaces;

/// <summary>
/// Abstraction over the file system. All real file operations in FileFlow go through
/// this interface; Core logic depends on it so it can be exercised with in-memory fakes.
/// Methods throw <see cref="FileFlow.Core.FileFlowException"/> with a classified
/// <see cref="FileFlow.Core.ErrorKind"/> instead of raw IOExceptions.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>Names of directories directly inside <paramref name="directoryPath"/> (no path part).</summary>
    IEnumerable<string> EnumerateDirectoryNames(string directoryPath);

    /// <summary>Names of files directly inside <paramref name="directoryPath"/> (no path part).</summary>
    IEnumerable<string> EnumerateFileNames(string directoryPath);

    /// <summary>Metadata for a file. Throws <see cref="FileFlow.Core.FileFlowException"/> when the file is missing or unreadable.</summary>
    FileMetadata GetFileMetadata(string path);

    void CreateDirectory(string path);

    /// <summary>Renames or moves a file. Throws on missing source or existing target.</summary>
    void MoveFile(string sourcePath, string targetPath);

    /// <summary>Copies a file. Throws on missing source or existing target.</summary>
    void CopyFile(string sourcePath, string targetPath);

    /// <summary>Deletes a file, optionally through the Windows Recycle Bin.</summary>
    void DeleteFile(string path, bool toRecycleBin);
}
