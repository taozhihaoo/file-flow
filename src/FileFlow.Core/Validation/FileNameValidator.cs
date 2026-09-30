using System.Text;

namespace FileFlow.Core.Validation;

/// <summary>
/// Validates Windows file names according to the platform rules:
/// forbidden characters, reserved device names, trailing dots/spaces, control characters and length.
/// </summary>
public static class FileNameValidator
{
    public const int MaxFileNameLength = 255;

    public static readonly IReadOnlyList<char> InvalidCharacters =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*',
    ];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Validates a file name (no directory part).
    /// Returns null when the name is valid, otherwise a user-readable reason.
    /// </summary>
    public static string? Validate(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "File name cannot be empty.";
        }

        if (fileName.AsSpan().IndexOfAny(InvalidCharacters.ToArray()) >= 0)
        {
            return $"File name contains forbidden characters ({string.Join(' ', InvalidCharacters)}).";
        }

        if (fileName.Any(c => char.IsControl(c)))
        {
            return "File name contains control characters.";
        }

        if (fileName.EndsWith('.') || fileName.EndsWith(' '))
        {
            return "File name cannot end with a dot or a space.";
        }

        var baseName = fileName;
        var dotIndex = fileName.IndexOf('.');
        if (dotIndex > 0)
        {
            baseName = fileName[..dotIndex];
        }

        if (ReservedNames.Contains(baseName))
        {
            return $"'{baseName}' is a reserved Windows device name.";
        }

        if (fileName.Length > MaxFileNameLength)
        {
            return $"File name is longer than {MaxFileNameLength} characters.";
        }

        return null;
    }

    /// <summary>True when the name passes <see cref="Validate"/>.</summary>
    public static bool IsValid(string? fileName) => Validate(fileName) is null;
}
