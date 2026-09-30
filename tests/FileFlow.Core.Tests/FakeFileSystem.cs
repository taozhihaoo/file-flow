using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;

namespace FileFlow.Core.Tests;

/// <summary>In-memory file system for deterministic Core tests. Paths are case-insensitive.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, FileMetadata> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, invoked before MoveFile; may throw to simulate file system errors.</summary>
    public Func<string, Exception>? MoveHook { get; set; }

    public FakeFileSystem AddDirectory(string path)
    {
        Directories.Add(Normalize(path));
        return this;
    }

    public FakeFileSystem AddFile(string path, long size = 100, bool hidden = false)
    {
        var normalized = Normalize(path);
        Files[normalized] = new FileMetadata(
            FullPath: normalized,
            Name: System.IO.Path.GetFileName(normalized),
            Extension: System.IO.Path.GetExtension(normalized),
            Length: size,
            CreatedAtUtc: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ModifiedAtUtc: new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc),
            IsHidden: hidden);
        var directory = System.IO.Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(directory))
        {
            Directories.Add(directory);
            var parent = System.IO.Path.GetDirectoryName(directory);
            if (parent == directory || string.IsNullOrEmpty(parent))
            {
                break;
            }

            directory = parent;
        }

        return this;
    }

    public bool DeleteFileToRecycleBinCalled { get; private set; }

    private static string Normalize(string path) => System.IO.Path.GetFullPath(path);

    public bool DirectoryExists(string path) => Directories.Contains(Normalize(path));

    public bool FileExists(string path) => Files.ContainsKey(Normalize(path));

    public IEnumerable<string> EnumerateDirectoryNames(string directoryPath)
    {
        var prefix = Normalize(directoryPath) + System.IO.Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directories)
        {
            if (directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !directory.Equals(Normalize(directoryPath), StringComparison.OrdinalIgnoreCase))
            {
                var rest = directory[prefix.Length..];
                var firstSegment = rest.Split(System.IO.Path.DirectorySeparatorChar)[0];
                if (seen.Add(firstSegment))
                {
                    yield return firstSegment;
                }
            }
        }
    }

    public IEnumerable<string> EnumerateFileNames(string directoryPath)
    {
        var prefix = Normalize(directoryPath) + System.IO.Path.DirectorySeparatorChar;
        foreach (var file in Files.Keys.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && file[prefix.Length..].Split(System.IO.Path.DirectorySeparatorChar).Length == 1)
            {
                yield return System.IO.Path.GetFileName(file);
            }
        }
    }

    public FileMetadata GetFileMetadata(string path)
    {
        if (!Files.TryGetValue(Normalize(path), out var metadata))
        {
            throw FileFlow.Core.FileFlowException.FileNotFound(path);
        }

        return metadata;
    }

    public void CreateDirectory(string path) => Directories.Add(Normalize(path));

    public void MoveFile(string sourcePath, string targetPath)
    {
        MoveHook?.Invoke(sourcePath);

        var source = Normalize(sourcePath);
        var target = Normalize(targetPath);
        if (!Files.TryGetValue(source, out var metadata))
        {
            throw FileFlow.Core.FileFlowException.FileNotFound(sourcePath);
        }

        if (Files.ContainsKey(target))
        {
            throw FileFlow.Core.FileFlowException.Conflict($"Target already exists: {targetPath}");
        }

        Files.Remove(source);
        var moved = metadata with { FullPath = target, Name = System.IO.Path.GetFileName(target) };
        Files[target] = moved;
    }

    public void CopyFile(string sourcePath, string targetPath)
    {
        var source = Normalize(sourcePath);
        var target = Normalize(targetPath);
        if (!Files.TryGetValue(source, out var metadata))
        {
            throw FileFlow.Core.FileFlowException.FileNotFound(sourcePath);
        }

        if (Files.ContainsKey(target))
        {
            throw FileFlow.Core.FileFlowException.Conflict($"Target already exists: {targetPath}");
        }

        var copied = metadata with { FullPath = target, Name = System.IO.Path.GetFileName(target) };
        Files[target] = copied;
    }

    public void DeleteFile(string path, bool toRecycleBin)
    {
        if (toRecycleBin)
        {
            DeleteFileToRecycleBinCalled = true;
        }

        if (!Files.Remove(Normalize(path)))
        {
            throw FileFlow.Core.FileFlowException.FileNotFound(path);
        }
    }
}
