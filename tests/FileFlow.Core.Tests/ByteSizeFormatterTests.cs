using Xunit;
using FileFlow.Core.Formatting;

namespace FileFlow.Core.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(2498560, "2.4 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    [InlineData(-5, "0 B")]
    public void Format_ProducesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
