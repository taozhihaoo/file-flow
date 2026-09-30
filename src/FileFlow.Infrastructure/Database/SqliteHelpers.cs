using Microsoft.Data.Sqlite;

namespace FileFlow.Infrastructure.Database;

/// <summary>Small parameterized-SQL helpers shared by the repositories.</summary>
public static class SqliteHelpers
{
    public static int ExecuteNonQuery(SqliteConnection connection, string sql, params SqliteParameter[] parameters)
    {
        using var command = CreateCommand(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(SqliteConnection connection, string sql, params SqliteParameter[] parameters)
    {
        using var command = CreateCommand(connection, sql, parameters);
        return command.ExecuteScalar();
    }

    public static SqliteCommand CreateCommand(SqliteConnection connection, string sql, params SqliteParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>Escapes LIKE wildcards in user input; combine with "ESCAPE '\'".</summary>
    public static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static string ToDateString(DateTime utc)
        => utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            : utc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static DateTime FromDateString(object? value)
    {
        var text = (value as string) ?? string.Empty;
        return DateTimeOffset.ParseExact(
            text,
            "O",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).UtcDateTime;
    }
}
