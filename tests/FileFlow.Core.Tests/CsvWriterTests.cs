using Xunit;
using FileFlow.Core.Csv;

namespace FileFlow.Core.Tests;

public class CsvWriterTests
{
    [Fact]
    public void WriteRow_PlainFieldsNeedNoQuotes()
    {
        Assert.Equal("a,b,c", CsvWriter.WriteRow(["a", "b", "c"]));
    }

    [Fact]
    public void WriteRow_QuotesFieldsWithCommas()
    {
        Assert.Equal("\"Smith, John\"", CsvWriter.WriteRow(["Smith, John"]));
    }

    [Fact]
    public void WriteRow_DoublesEmbeddedQuotes()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.WriteRow(["say \"hi\""]));
    }

    [Fact]
    public void WriteRow_QuotesNewlines()
    {
        Assert.Equal("\"line1\nline2\"", CsvWriter.WriteRow(["line1\nline2"]));
    }

    [Fact]
    public void WriteRow_HandlesNullAndEmpty()
    {
        Assert.Equal(",", CsvWriter.WriteRow([null, ""]));
    }

    [Fact]
    public void EscapeThenParse_RoundTrips()
    {
        var original = new[] { "plain", "with,comma", "with\"quote", "with\nnewline", "" };
        var csv = CsvWriter.WriteRow(original);
        var parsed = CsvParser.Parse(csv);
        Assert.Equal(original, parsed.Rows[0].Fields);
    }
}
