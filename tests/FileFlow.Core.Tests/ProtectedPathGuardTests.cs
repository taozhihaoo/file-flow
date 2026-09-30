using Xunit;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Tests;

public class ProtectedPathGuardTests
{
    private static readonly string[] Roots = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"];

    [Theory]
    [InlineData(@"C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\WINDOWS\web\wallpaper.jpg")]
    [InlineData(@"C:\Program Files\Common Files\shared.dll")]
    [InlineData(@"c:\program files (x86)\app\tool.exe")]
    public void IsProtected_DetectsSystemPaths(string path)
    {
        Assert.True(ProtectedPathGuard.IsProtected(path, Roots));
    }

    [Theory]
    [InlineData(@"C:\Users\demo\Documents\report.pdf")]
    [InlineData(@"D:\data\file.txt")]
    [InlineData(@"C:\Windows-Backup\important.bak")] // path boundary must not be confused
    public void IsProtected_AllowsUserPaths(string path)
    {
        Assert.False(ProtectedPathGuard.IsProtected(path, Roots));
    }

    [Fact]
    public void FindProtectedRoot_ReturnsTheMatchingRoot()
    {
        var root = ProtectedPathGuard.FindProtectedRoot(@"C:\Windows\Temp\test.txt", Roots);
        Assert.Equal(@"C:\Windows", root, ignoreCase: true);
    }

    [Fact]
    public void FindProtectedRoot_ReturnsNullForUnprotected()
    {
        Assert.Null(ProtectedPathGuard.FindProtectedRoot(@"C:\Users\demo\file.txt", Roots));
    }

    [Fact]
    public void IsProtected_HandlesNullAndEmpty()
    {
        Assert.False(ProtectedPathGuard.IsProtected("", Roots));
        Assert.False(ProtectedPathGuard.IsProtected(null!, Roots));
    }

    [Fact]
    public void DefaultRoots_CoverCriticalDirectories()
    {
        Assert.Contains(@"C:\Windows", ProtectedPathGuard.DefaultProtectedRoots);
        Assert.Contains(@"C:\Program Files", ProtectedPathGuard.DefaultProtectedRoots);
    }
}
