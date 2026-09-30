namespace FileFlow.Core.Validation;

/// <summary>
/// Identifies critical system directories. FileFlow refuses to run batch destructive
/// operations on these by default; the UI can ask for an extra confirmation instead.
/// </summary>
public static class ProtectedPathGuard
{
    /// <summary>Default protected roots for a standard Windows installation.</summary>
    public static IReadOnlyList<string> DefaultProtectedRoots { get; } =
    [
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
    ];

    /// <summary>
    /// Returns the protected root that contains <paramref name="fullPath"/>, or null when the path is not protected.
    /// Comparison is case-insensitive and requires a real path boundary (C:\Windows does not match C:\Windows-Backup).
    /// </summary>
    public static string? FindProtectedRoot(string fullPath, IEnumerable<string> protectedRoots)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = System.IO.Path.GetFullPath(fullPath)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var root in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string normalizedRoot;
            try
            {
                normalizedRoot = System.IO.Path.GetFullPath(root)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                continue;
            }

            if (normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && (normalized.Length == normalizedRoot.Length
                    || normalized[normalizedRoot.Length] == System.IO.Path.DirectorySeparatorChar))
            {
                return normalizedRoot;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="fullPath"/> lives inside one of the protected roots.</summary>
    public static bool IsProtected(string fullPath, IEnumerable<string> protectedRoots)
        => FindProtectedRoot(fullPath, protectedRoots) is not null;
}
