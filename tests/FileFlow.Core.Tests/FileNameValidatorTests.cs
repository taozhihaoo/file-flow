using Xunit;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Tests;

public class FileNameValidatorTests
{
    [Theory]
    [InlineData("invoice.pdf")]
    [InlineData("2026_report.docx")]
    [InlineData(".gitignore")]
    [InlineData("archive.tar.gz")]
    [InlineData("no-extension")]
    public void Validate_AcceptsCommonNames(string name)
    {
        Assert.Null(FileNameValidator.Validate(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsEmpty(string? name)
    {
        Assert.NotNull(FileNameValidator.Validate(name));
    }

    [Theory]
    [InlineData("a<b.txt")]
    [InlineData("a>b.txt")]
    [InlineData("a:b.txt")]
    [InlineData("a\"b.txt")]
    [InlineData("a/b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("a|b.txt")]
    [InlineData("a?b.txt")]
    [InlineData("a*b.txt")]
    public void Validate_RejectsForbiddenCharacters(string name)
    {
        var error = FileNameValidator.Validate(name);
        Assert.NotNull(error);
        Assert.Contains("forbidden characters", error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("Con.txt")]
    [InlineData("PRN")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Validate_RejectsReservedDeviceNames(string name)
    {
        Assert.NotNull(FileNameValidator.Validate(name));
    }

    [Theory]
    [InlineData("file.")]
    [InlineData("file ")]
    public void Validate_RejectsTrailingDotOrSpace(string name)
    {
        Assert.NotNull(FileNameValidator.Validate(name));
    }

    [Fact]
    public void Validate_RejectsControlCharacters()
    {
        Assert.NotNull(FileNameValidator.Validate("bad\u0007name.txt"));
    }

    [Fact]
    public void Validate_RejectsTooLongNames()
    {
        var name = new string('a', 256) + ".txt";
        var error = FileNameValidator.Validate(name);
        Assert.NotNull(error);
        Assert.Contains("255", error);
    }

    [Fact]
    public void Validate_AcceptsMaxLengthName()
    {
        var name = new string('a', 251) + ".txt"; // 255 chars total
        Assert.Null(FileNameValidator.Validate(name));
    }

    [Fact]
    public void IsValid_MatchesValidate()
    {
        Assert.True(FileNameValidator.IsValid("ok.txt"));
        Assert.False(FileNameValidator.IsValid("bad?.txt"));
    }
}
