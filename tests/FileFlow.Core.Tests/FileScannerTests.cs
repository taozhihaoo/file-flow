using Xunit;
using FileFlow.Core.Models;
using FileFlow.Core.Scanning;

namespace FileFlow.Core.Tests;

public class FileScannerTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FileScanner _scanner;

    public FileScannerTests()
    {
        _scanner = new FileScanner(_fs);
    }

    [Fact]
    public void Scan_FindsFilesRecursively()
    {
        _fs.AddDirectory(@"C:\root");
        _fs.AddDirectory(@"C:\root\sub");
        _fs.AddFile(@"C:\root\a.txt", size: 10);
        _fs.AddFile(@"C:\root\sub\b.pdf", size: 20);

        var result = _scanner.Scan(@"C:\root", new ScanOptions(Recursive: true), null, CancellationToken.None);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(2, result.DirectoriesVisited);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Files, f => f.FileName == "a.txt" && f.SizeBytes == 10);
    }

    [Fact]
    public void Scan_NonRecursiveOnlyScansRoot()
    {
        _fs.AddDirectory(@"C:\root");
        _fs.AddDirectory(@"C:\root\sub");
        _fs.AddFile(@"C:\root\a.txt");
        _fs.AddFile(@"C:\root\sub\b.txt");

        var result = _scanner.Scan(@"C:\root", new ScanOptions(Recursive: false), null, CancellationToken.None);

        Assert.Single(result.Files);
        Assert.Equal("a.txt", result.Files[0].FileName);
    }

    [Fact]
    public void Scan_SkipsHiddenFilesUnlessRequested()
    {
        _fs.AddDirectory(@"C:\root");
        _fs.AddFile(@"C:\root\visible.txt");
        _fs.AddFile(@"C:\root\hidden.txt", hidden: true);

        var normal = _scanner.Scan(@"C:\root", new ScanOptions(Recursive: true, IncludeHidden: false), null, CancellationToken.None);
        var withHidden = _scanner.Scan(@"C:\root", new ScanOptions(Recursive: true, IncludeHidden: true), null, CancellationToken.None);

        Assert.Single(normal.Files);
        Assert.Equal(2, withHidden.Files.Count);
    }

    [Fact]
    public void Scan_NormalizesExtensionToLowercase()
    {
        _fs.AddDirectory(@"C:\root");
        _fs.AddFile(@"C:\root\photo.JPG");

        var result = _scanner.Scan(@"C:\root", new ScanOptions(), null, CancellationToken.None);

        Assert.Equal(".jpg", Assert.Single(result.Files).Extension);
    }

    [Fact]
    public void Scan_RecordsMetadata()
    {
        _fs.AddDirectory(@"C:\root");
        _fs.AddFile(@"C:\root\a.txt");

        var result = _scanner.Scan(@"C:\root", new ScanOptions(), null, CancellationToken.None);
        var file = Assert.Single(result.Files);

        Assert.Equal(@"C:\root\a.txt", file.FullPath);
        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), file.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc), file.ModifiedAtUtc);
        Assert.True(file.ScannedAtUtc <= DateTime.UtcNow && file.ScannedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.False(file.IsMissing);
    }

    [Fact]
    public void Scan_MissingRootThrows()
    {
        Assert.Throws<FileFlow.Core.FileFlowException>(
            () => _scanner.Scan(@"C:\no-such-root", new ScanOptions(), null, CancellationToken.None));
    }

    [Fact]
    public void Scan_EmptyRootPathThrows()
    {
        Assert.Throws<FileFlow.Core.FileFlowException>(
            () => _scanner.Scan("", new ScanOptions(), null, CancellationToken.None));
    }

    [Fact]
    public void Scan_CancellationThrows()
    {
        _fs.AddDirectory(@"C:\root");
        for (var i = 0; i < 100; i++)
        {
            _fs.AddFile($@"C:\root\file{i}.txt");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => _scanner.Scan(@"C:\root", new ScanOptions(), null, cts.Token));
    }

    [Fact]
    public void Scan_ReportsProgress()
    {
        _fs.AddDirectory(@"C:\root");
        for (var i = 0; i < 120; i++)
        {
            _fs.AddFile($@"C:\root\file{i}.txt");
        }

        // Synchronous recorder: Progress<T> would post to the thread pool and race the assertion.
        var reports = new List<ScanProgress>();
        var recorder = new InlineRecorder(reports.Add);
        _scanner.Scan(@"C:\root", new ScanOptions(), recorder, CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(120, reports[^1].FilesFound);
    }

    private sealed class InlineRecorder(Action<ScanProgress> callback) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => callback(value);
    }
}
