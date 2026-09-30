using Xunit;
using FileFlow.Core;
using FileFlow.Core.Scanning;
using FileFlow.Infrastructure.FileSystem;
using FileFlow.Infrastructure.Services;

namespace FileFlow.Infrastructure.Tests;

public class FileServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileService _service;

    public FileServiceTests()
    {
        _service = new FileService(new WindowsFileSystem(), new KeyedLockRegistry());
    }

    [Fact]
    public void Rename_MovesFile()
    {
        var source = _temp.WriteFileAndGetPath("a.txt");
        var target = _temp.FilePath("b.txt");

        _service.Rename(source, target);

        Assert.False(System.IO.File.Exists(source));
        Assert.True(System.IO.File.Exists(target));
    }

    [Fact]
    public void Rename_RejectsInvalidPaths()
    {
        var ex = Assert.Throws<FileFlowException>(() => _service.Rename("bad?name<.txt", _temp.FilePath("x.txt")));
        Assert.Equal(ErrorKind.PathValidation, ex.Kind);
    }

    [Fact]
    public void Rename_MissingSourceThrowsNotFound()
    {
        var ex = Assert.Throws<FileFlowException>(
            () => _service.Rename(_temp.FilePath("missing.txt"), _temp.FilePath("x.txt")));
        Assert.Equal(ErrorKind.FileNotFound, ex.Kind);
    }

    [Fact]
    public void Rename_ExistingTargetThrowsConflict()
    {
        var source = _temp.WriteFileAndGetPath("a.txt");
        _temp.WriteFile("b.txt");

        var ex = Assert.Throws<FileFlowException>(() => _service.Rename(source, _temp.FilePath("b.txt")));
        Assert.Equal(ErrorKind.OperationConflict, ex.Kind);
    }

    [Fact]
    public void Rename_CaseOnlyChangeSucceeds()
    {
        var source = _temp.WriteFileAndGetPath("Case.TXT");
        var target = _temp.FilePath("case.txt");

        _service.Rename(source, target);

        Assert.True(System.IO.File.Exists(target));
    }

    [Fact]
    public void Copy_DuplicatesFile()
    {
        var source = _temp.WriteFileAndGetPath("orig.txt");
        var target = _temp.FilePath("copy.txt");

        _service.Copy(source, target);

        Assert.True(System.IO.File.Exists(source));
        Assert.True(System.IO.File.Exists(target));
    }

    [Fact]
    public void Copy_ExistingTargetThrowsConflict()
    {
        var source = _temp.WriteFileAndGetPath("orig.txt");
        _temp.WriteFile("copy.txt");

        var ex = Assert.Throws<FileFlowException>(() => _service.Copy(source, _temp.FilePath("copy.txt")));
        Assert.Equal(ErrorKind.OperationConflict, ex.Kind);
    }

    [Fact]
    public void Delete_PermanentRemovesFile()
    {
        var path = _temp.WriteFileAndGetPath("doomed.txt");

        _service.Delete(path, toRecycleBin: false);

        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void Delete_MissingThrowsNotFound()
    {
        var ex = Assert.Throws<FileFlowException>(
            () => _service.Delete(_temp.FilePath("nope.txt"), toRecycleBin: false));
        Assert.Equal(ErrorKind.FileNotFound, ex.Kind);
    }

    [Fact]
    public async Task Rename_ProtectsAgainstConcurrentOperationsOnSamePath()
    {
        // Two threads rename the same source to different targets; exactly one may win.
        var source = _temp.WriteFileAndGetPath("contested.txt");
        var barrier = new System.Threading.Barrier(2);
        var wins = 0;

        var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            try
            {
                _service.Rename(source, _temp.FilePath($"target{i}.txt"));
                Interlocked.Increment(ref wins);
            }
            catch (FileFlowException)
            {
                // lost the race - expected for one of the threads
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, wins);
    }

    public void Dispose() => _temp.Dispose();
}
