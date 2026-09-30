using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Validation;

namespace FileFlow.Infrastructure.Services;

/// <summary>
/// Single-file operations with full validation and per-path locking.
/// Throws <see cref="FileFlowException"/> with a classified kind on every failure;
/// never overwrites an existing target.
/// </summary>
public sealed class FileService : IFileService
{
    private readonly IFileSystem _fileSystem;
    private readonly KeyedLockRegistry _pathLocks;

    public FileService(IFileSystem fileSystem, KeyedLockRegistry pathLocks)
    {
        _fileSystem = fileSystem;
        _pathLocks = pathLocks;
    }

    public void Rename(string sourcePath, string targetPath)
    {
        EnsureValidPath(sourcePath, "source");
        EnsureValidPath(targetPath, "target");

        // Case-only renames (A.txt -> a.txt) are legal on Windows; nothing else may collide.
        var caseOnlyRename = string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase);

        using var scope = _pathLocks.AcquireAll(sourcePath, targetPath);

        if (!_fileSystem.FileExists(sourcePath))
        {
            throw FileFlowException.FileNotFound(sourcePath);
        }

        if (!caseOnlyRename && _fileSystem.FileExists(targetPath))
        {
            throw FileFlowException.Conflict($"Target already exists: {targetPath}");
        }

        _fileSystem.MoveFile(sourcePath, targetPath);
    }

    public void Copy(string sourcePath, string targetPath)
    {
        EnsureValidPath(sourcePath, "source");
        EnsureValidPath(targetPath, "target");

        using var scope = _pathLocks.AcquireAll(sourcePath, targetPath);

        if (!_fileSystem.FileExists(sourcePath))
        {
            throw FileFlowException.FileNotFound(sourcePath);
        }

        if (_fileSystem.FileExists(targetPath))
        {
            throw FileFlowException.Conflict($"Target already exists: {targetPath}");
        }

        _fileSystem.CopyFile(sourcePath, targetPath);
    }

    public void Delete(string path, bool toRecycleBin)
    {
        EnsureValidPath(path, "source");

        using var scope = _pathLocks.Acquire(path);

        if (!_fileSystem.FileExists(path))
        {
            throw FileFlowException.FileNotFound(path);
        }

        _fileSystem.DeleteFile(path, toRecycleBin);
    }

    private static void EnsureValidPath(string path, string role)
    {
        var error = PathValidator.ValidateFullPath(path);
        if (error is not null)
        {
            throw FileFlowException.PathValidation($"Invalid {role} path: {error}");
        }
    }
}
