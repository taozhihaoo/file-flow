using FileFlow.Infrastructure;
using FileFlow.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

/// <summary>Creates a unique temporary directory, removed on dispose. No user files are touched.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fileflow-tests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string FilePath(string name) => System.IO.Path.Combine(Path, name);

    public string SubDirectory(string name)
    {
        var path = FilePath(name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a small text file with deterministic content.</summary>
    public string WriteFileAndGetPath(string relativePath, string content = "test content")
    {
        var fullPath = FilePath(relativePath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        System.IO.File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void WriteFile(string relativePath, string content = "test content")
        => WriteFileAndGetPath(relativePath, content);

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort cleanup
        }
    }
}

/// <summary>A migrated, throwaway SQLite database backed by a temp directory.</summary>
public sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        Directory = new TempDirectory();
        Factory = new SQLiteDatabase(System.IO.Path.Combine(Directory.Path, "test.db"));
        new DbMigrator(Factory, NullLogger<DbMigrator>.Instance).Migrate();
    }

    public TempDirectory Directory { get; }

    public SQLiteDatabase Factory { get; }

    public void Dispose() => Directory.Dispose();
}
