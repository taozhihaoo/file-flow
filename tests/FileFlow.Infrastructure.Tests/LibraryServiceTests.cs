using Xunit;
using FileFlow.Core.Models;
using FileFlow.Core.Scanning;
using FileFlow.Infrastructure;
using FileFlow.Infrastructure.FileSystem;
using FileFlow.Infrastructure.Repositories;
using FileFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlow.Infrastructure.Tests;

public class LibraryServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TestDatabase _db = new();
    private readonly FileRepository _files;
    private readonly LibraryService _library;

    public LibraryServiceTests()
    {
        _files = new FileRepository(_db.Factory);
        var history = new HistoryRepository(_db.Factory);
        var fileSystem = new WindowsFileSystem();
        _library = new LibraryService(
            new FileScanner(fileSystem),
            _files,
            history,
            fileSystem,
            NullLogger<LibraryService>.Instance);
    }

    [Fact]
    public async Task ScanDirectory_InsertsAllFilesAndRecordsHistory()
    {
        _temp.WriteFile("docs\\a.txt");
        _temp.WriteFile("docs\\b.pdf");
        _temp.WriteFile("media\\c.jpg");

        var result = await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);

        Assert.Equal(3, result.Files.Count);
        Assert.Equal(3, await _files.CountAllAsync());

        var history = new HistoryRepository(_db.Factory);
        var scans = await history.GetByTypeAsync("Scan", 10);
        Assert.Single(scans);
        Assert.Equal(3, scans[0].SuccessCount);
    }

    [Fact]
    public async Task ScanDirectory_MarksVanishedFilesAsMissing()
    {
        _temp.WriteFile("kept.txt");
        _temp.WriteFile("gone.txt");
        await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);

        System.IO.File.Delete(_temp.FilePath("gone.txt"));
        await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);

        var kept = await _files.GetByPathAsync(_temp.FilePath("kept.txt"));
        var gone = await _files.GetByPathAsync(_temp.FilePath("gone.txt"));
        Assert.False(kept!.IsMissing);
        Assert.True(gone!.IsMissing);
        Assert.Equal(1, await _files.CountMissingAsync());
    }

    [Fact]
    public async Task ScanDirectory_UpdatesExistingRecords()
    {
        _temp.WriteFile("a.txt", "small");
        await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);
        var before = await _files.GetByPathAsync(_temp.FilePath("a.txt"));

        System.IO.File.WriteAllText(_temp.FilePath("a.txt"), "much larger content now");
        await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);

        var after = await _files.GetByPathAsync(_temp.FilePath("a.txt"));
        Assert.Equal(before!.Id, after!.Id);
        Assert.Equal(new System.IO.FileInfo(_temp.FilePath("a.txt")).Length, after.SizeBytes);
        Assert.Equal(1, await _files.CountAllAsync()); // no duplicate rows for a.txt
    }

    [Fact]
    public async Task ScanDirectory_MissingRootThrows()
    {
        await Assert.ThrowsAsync<FileFlow.Core.FileFlowException>(
            () => _library.ScanDirectoryAsync(_temp.FilePath("nope"), new ScanOptions(Recursive: true), null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshMissing_UpdatesFlags()
    {
        _temp.WriteFile("a.txt");
        _temp.WriteFile("b.txt");
        await _library.ScanDirectoryAsync(_temp.Path, new ScanOptions(Recursive: true), null, CancellationToken.None);
        var a = await _files.GetByPathAsync(_temp.FilePath("a.txt"));
        var b = await _files.GetByPathAsync(_temp.FilePath("b.txt"));
        Assert.NotNull(a);
        Assert.NotNull(b);
        await _files.SetMissingAsync(a.Id, true); // pretend it is missing

        var changed = await _library.RefreshMissingAsync([a.Id, b!.Id]);

        Assert.Equal(1, changed);
        Assert.False((await _files.GetByPathAsync(_temp.FilePath("a.txt")))!.IsMissing);
    }

    public void Dispose()
    {
        _db.Dispose();
        _temp.Dispose();
    }
}
