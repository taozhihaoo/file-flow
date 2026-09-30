using System.Text;

namespace FileFlow.Core.Csv;

/// <summary>Result of parsing CSV text.</summary>
public sealed class CsvParseResult
{
    private readonly List<(int RowNumber, string[] Fields)> _rows = new();
    private readonly List<string> _errors = new();

    public IReadOnlyList<(int RowNumber, string[] Fields)> Rows => _rows;

    public IReadOnlyList<string> Errors => _errors;

    internal void AddRow(int rowNumber, string[] fields) => _rows.Add((rowNumber, fields));

    internal void AddError(string message) => _errors.Add(message);
}

/// <summary>
/// Minimal RFC 4180 CSV parser: quoted fields, escaped quotes (""), comma and
/// CR/LF/CRLF line endings. Row numbers are 1-based and count every physical line
/// so import errors can point at the exact input row.
/// </summary>
public static class CsvParser
{
    public static CsvParseResult Parse(TextReader reader)
    {
        var result = new CsvParseResult();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var fieldStarted = false;
        var rowNumber = 1;
        var c = reader.Read();

        while (c >= 0)
        {
            var ch = (char)c;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"' && field.Length == 0 && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                row.Add(field.ToString());
                result.AddRow(rowNumber, row.ToArray());
                row.Clear();
                field.Clear();
                fieldStarted = false;
                rowNumber++;
            }
            else
            {
                field.Append(ch);
                fieldStarted = true;
            }

            c = reader.Read();
        }

        if (inQuotes)
        {
            result.AddError($"Row {rowNumber}: quoted field is not terminated.");
        }

        // Flush the final row when the file does not end with a newline.
        if (field.Length > 0 || fieldStarted || row.Count > 0)
        {
            row.Add(field.ToString());
            result.AddRow(rowNumber, row.ToArray());
        }

        return result;
    }

    /// <summary>Parses CSV from a string; the first physical line is row 1.</summary>
    public static CsvParseResult Parse(string csv)
    {
        using var reader = new StringReader(csv);
        return Parse(reader);
    }
}
