namespace FileFlow.Core.Validation;

/// <summary>
/// Validates absolute Windows paths used as scan roots and operation targets.
/// Pure string checks only - no file system access.
/// </summary>
public static class PathValidator
{
    public const int MaxPathLength = 4096;

    /// <summary>
    /// Validates a full path. Returns null when valid, otherwise a user-readable reason.
    /// Requires absolute local paths; UNC/network paths are not supported by FileFlow.
    /// </summary>
    public static string? ValidateFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Path cannot be empty.";
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith(@"//", StringComparison.Ordinal))
        {
            return "UNC/network paths are not supported.";
        }

        if (!System.IO.Path.IsPathFullyQualified(trimmed))
        {
            return "Path must be absolute (include the drive letter).";
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or System.IO.PathTooLongException
                                   or NotSupportedException)
        {
            return "Path contains invalid characters or is malformed.";
        }

        if (fullPath.Length > MaxPathLength)
        {
            return $"Path is longer than {MaxPathLength} characters.";
        }

        foreach (var segment in fullPath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        {
            // Only actual file/dir name segments are checked; drive letters like "C:" are skipped.
            if (segment.Length == 0 || segment.EndsWith(':'))
            {
                continue;
            }

            var reason = FileNameValidator.Validate(segment);
            if (reason is not null)
            {
                return $"Invalid path segment '{segment}': {reason}";
            }
        }

        return null;
    }

    /// <summary>Returns null when valid, otherwise a user-readable reason.</summary>
    public static string? ValidateDirectoryPath(string? path) => ValidateFullPath(path);
}
