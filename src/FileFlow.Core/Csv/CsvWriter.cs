using System.Text;

namespace FileFlow.Core.Csv;

/// <summary>RFC 4180 compliant CSV field quoting.</summary>
public static class CsvWriter
{
    public static string EscapeField(string? field)
    {
        var value = field ?? string.Empty;
        var needsQuotes = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string WriteRow(IEnumerable<string?> fields)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(EscapeField(field));
        }

        return builder.ToString();
    }
}
