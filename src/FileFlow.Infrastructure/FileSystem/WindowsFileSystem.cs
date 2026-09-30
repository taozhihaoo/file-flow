using System.Runtime.Versioning;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;

namespace FileFlow.Infrastructure.FileSystem;

/// <summary>
/// Production <see cref="IFileSystem"/> backed by System.IO.
/// Every method maps raw exceptions into classified <see cref="FileFlowException"/>s.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectoryNames(string directoryPath)
        => Directory.EnumerateDirectories(directoryPath).Select(System.IO.Path.GetFileName)!;

    public IEnumerable<string> EnumerateFileNames(string directoryPath)
        => Directory.EnumerateFiles(directoryPath).Select(System.IO.Path.GetFileName)!;

    public FileMetadata GetFileMetadata(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw FileFlowException.FileNotFound(path);
            }

            var isHidden = (info.Attributes & System.IO.FileAttributes.Hidden) != 0;
            return new FileMetadata(
                FullPath: info.FullName,
                Name: info.Name,
                Extension: info.Extension,
                Length: info.Length,
                CreatedAtUtc: info.CreationTimeUtc,
                ModifiedAtUtc: info.LastWriteTimeUtc,
                IsHidden: isHidden);
        }
        catch (Exception ex)
        {
            throw ExceptionMapper.Map(ex, path);
        }
    }

    public void CreateDirectory(string path)
        => ExceptionMapper.Run(() => Directory.CreateDirectory(path), path);

    public void MoveFile(string sourcePath, string targetPath)
        => ExceptionMapper.Run(() => File.Move(sourcePath, targetPath, overwrite: false), sourcePath);

    public void CopyFile(string sourcePath, string targetPath)
        => ExceptionMapper.Run(() => File.Copy(sourcePath, targetPath, overwrite: false), sourcePath);

    public void DeleteFile(string path, bool toRecycleBin)
        => ExceptionMapper.Run(
            () =>
            {
                // File.Delete is a silent no-op for missing files; we want a classified error.
                if (!File.Exists(path))
                {
                    throw FileFlowException.FileNotFound(path);
                }

                if (toRecycleBin)
                {
                    RecycleBin.SendFileToRecycleBin(path);
                }
                else
                {
                    File.Delete(path);
                }
            },
            path);
}
