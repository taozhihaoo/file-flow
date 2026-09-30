using Xunit;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Tests;

public class PathValidatorTests
{
    [Fact]
    public void ValidateFullPath_AcceptsAbsoluteWindowsPath()
    {
        Assert.Null(PathValidator.ValidateFullPath(@"C:\Users\demo\Documents\report.pdf"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateFullPath_RejectsEmpty(string? path)
    {
        Assert.NotNull(PathValidator.ValidateFullPath(path));
    }

    [Theory]
    [InlineData("relative\\path.txt")]
    [InlineData("\\\\server\\share\\file.txt")] // UNC paths are out of scope for FileFlow
    public void ValidateFullPath_RejectsNonAbsolutePaths(string path)
    {
        Assert.NotNull(PathValidator.ValidateFullPath(path));
    }

    [Fact]
    public void ValidateFullPath_RejectsInvalidSegment()
    {
        var error = PathValidator.ValidateFullPath(@"C:\data\bad?name.txt");
        Assert.NotNull(error);
        Assert.Contains("bad?name.txt", error);
    }

    [Fact]
    public void ValidateFullPath_RejectsTooLongPath()
    {
        var path = @"C:\" + new string('d', 5000) + ".txt";
        Assert.NotNull(PathValidator.ValidateFullPath(path));
    }

    [Fact]
    public void ValidateDirectoryPath_AcceptsDriveRoot()
    {
        Assert.Null(PathValidator.ValidateDirectoryPath(@"C:\"));
    }
}
