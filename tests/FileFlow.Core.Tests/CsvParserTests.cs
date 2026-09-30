using Xunit;
using FileFlow.Core.Csv;

namespace FileFlow.Core.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parse_SimpleRows()
    {
        var result = CsvParser.Parse("a,b,c\n1,2,3\n");
        Assert.Equal(2, result.Rows.Count);
        Assert.Empty(result.Errors);
        Assert.Equal(["a", "b", "c"], result.Rows[0].Fields);
        Assert.Equal(["1", "2", "3"], result.Rows[1].Fields);
    }

    [Fact]
    public void Parse_QuotedFieldWithComma()
    {
        var result = CsvParser.Parse("name,note\n\"Smith, John\",\"a, b\"\n");
        Assert.Equal(["Smith, John", "a, b"], result.Rows[1].Fields);
    }

    [Fact]
    public void Parse_EscapedQuotes()
    {
        var result = CsvParser.Parse("\"he said \"\"hello\"\" to me\"\n");
        Assert.Equal(["he said \"hello\" to me"], result.Rows[0].Fields);
    }

    [Fact]
    public void Parse_HandlesCrLfAndLf()
    {
        var result = CsvParser.Parse("a,b\r\nc,d\ne,f");
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(["e", "f"], result.Rows[2].Fields);
    }

    [Fact]
    public void Parse_EmptyFields()
    {
        var result = CsvParser.Parse("a,,c\n,,\n");
        Assert.Equal(["a", "", "c"], result.Rows[0].Fields);
        Assert.Equal(["", "", ""], result.Rows[1].Fields);
    }

    [Fact]
    public void Parse_TracksRowNumbersAcrossQuotedNewlines()
    {
        // The second row contains a quoted newline: it is still row 2 for the parser.
        var result = CsvParser.Parse("x,y\n\"line1\nline2\",z\nlast,row\n");
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(2, result.Rows[1].RowNumber);
        Assert.Equal(3, result.Rows[2].RowNumber);
    }

    [Fact]
    public void Parse_ReportsUnterminatedQuote()
    {
        var result = CsvParser.Parse("a,\"unterminated\n");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_EmptyInput_ProducesNoRows()
    {
        var result = CsvParser.Parse("");
        Assert.Empty(result.Rows);
    }
}
