using Xunit;
using FileFlow.Core;
using FileFlow.Core.Models;
using FileFlow.Infrastructure.FileSystem;

namespace FileFlow.Infrastructure.Tests;

public class WindowsFileSystemTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly WindowsFileSystem _fs = new();

    [Fact]
    public void Exists_ChecksFilesAndDirectories()
    {
        var file = _temp.WriteFileAndGetPath("a.txt");
        var dir = _temp.SubDirectory("sub");

        Assert.True(_fs.FileExists(file));
        Assert.True(_fs.DirectoryExists(dir));
        Assert.False(_fs.FileExists(_temp.FilePath("missing.txt")));
        Assert.False(_fs.DirectoryExists(_temp.FilePath("missing-dir")));
    }

    [Fact]
    public void GetFileMetadata_ReturnsDetails()
    {
        _temp.WriteFile("doc.txt", "hello world");
        var path = _temp.FilePath("doc.txt");

        var metadata = _fs.GetFileMetadata(path);

        Assert.Equal("doc.txt", metadata.Name);
        Assert.Equal(".txt", metadata.Extension);
        Assert.Equal(11, metadata.Length);
        Assert.False(metadata.IsHidden);
        Assert.True(metadata.ModifiedAtUtc > DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void GetFileMetadata_MissingFileThrowsNotFound()
    {
        var ex = Assert.Throws<FileFlowException>(() => _fs.GetFileMetadata(_temp.FilePath("nope.txt")));
        Assert.Equal(ErrorKind.FileNotFound, ex.Kind);
    }

    [Fact]
    public void MoveFile_RenamesFile()
    {
        var source = _temp.WriteFileAndGetPath("old.txt");
        var target = _temp.FilePath("new.txt");

        _fs.MoveFile(source, target);

        Assert.False(_fs.FileExists(source));
        Assert.True(_fs.FileExists(target));
    }

    [Fact]
    public void MoveFile_CaseOnlyRenameWorks()
    {
        var source = _temp.WriteFileAndGetPath("Report.TXT");
        var target = _temp.FilePath("report.txt");

        _fs.MoveFile(source, target);

        Assert.True(_fs.FileExists(target));
        Assert.Single(System.IO.Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void MoveFile_OntoExistingTargetThrowsConflict()
    {
        var source = _temp.WriteFileAndGetPath("a.txt");
        _temp.WriteFile("b.txt");

        var ex = Assert.Throws<FileFlowException>(() => _fs.MoveFile(source, _temp.FilePath("b.txt")));
        Assert.Equal(ErrorKind.OperationConflict, ex.Kind);
    }

    [Fact]
    public void CopyFile_CreatesDuplicate()
    {
        var source = _temp.WriteFileAndGetPath("orig.txt");
        var target = _temp.FilePath("copy.txt");

        _fs.CopyFile(source, target);

        Assert.True(_fs.FileExists(source));
        Assert.True(_fs.FileExists(target));
    }

    [Fact]
    public void DeleteFile_PermanentRemoves()
    {
        var path = _temp.WriteFileAndGetPath("doomed.txt");

        _fs.DeleteFile(path, toRecycleBin: false);

        Assert.False(_fs.FileExists(path));
    }

    [Fact]
    public void DeleteFile_MissingThrowsNotFound()
    {
        var ex = Assert.Throws<FileFlowException>(
            () => _fs.DeleteFile(_temp.FilePath("ghost.txt"), toRecycleBin: false));
        Assert.Equal(ErrorKind.FileNotFound, ex.Kind);
    }

    [Fact]
    public void DeleteFile_ReadOnlyFileIsMappedToAccessError()
    {
        var path = _temp.WriteFileAndGetPath("locked.txt");
        System.IO.File.SetAttributes(path, System.IO.FileAttributes.ReadOnly);

        var ex = Assert.Throws<FileFlowException>(() => _fs.DeleteFile(path, toRecycleBin: false));
        Assert.Equal(ErrorKind.FileAccess, ex.Kind);

        // clean up so the temp dir can be deleted
        System.IO.File.SetAttributes(path, System.IO.FileAttributes.Normal);
    }

    [Fact]
    public void EnumerateNames_ListsDirectChildren()
    {
        _temp.SubDirectory("child-a");
        _temp.SubDirectory("child-b");
        _temp.WriteFile("child-a\\file1.txt");
        _temp.WriteFile("file2.txt");

        var dirs = _fs.EnumerateDirectoryNames(_temp.Path).ToList();
        var files = _fs.EnumerateFileNames(_temp.Path).ToList();

        Assert.Equal(["child-a", "child-b"], dirs.OrderBy(d => d).ToArray());
        Assert.Equal(["file2.txt"], files);
    }

    public void Dispose() => _temp.Dispose();
}
