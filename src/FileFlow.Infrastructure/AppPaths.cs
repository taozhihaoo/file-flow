using FileFlow.Core.Interfaces;

namespace FileFlow.Infrastructure;

/// <summary>
/// Application data locations. Default root is %LOCALAPPDATA%\FileFlow;
/// tests and portable usage can override the root.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? dataDirectoryOverride = null)
    {
        var root = dataDirectoryOverride
                   ?? System.IO.Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "FileFlow");
        DataDirectory = root;
        DatabasePath = System.IO.Path.Combine(root, "fileflow.db");
        LogsDirectory = System.IO.Path.Combine(root, "logs");
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    /// <summary>Creates the data and log directories when missing.</summary>
    public void EnsureDirectories()
    {
        System.IO.Directory.CreateDirectory(DataDirectory);
        System.IO.Directory.CreateDirectory(LogsDirectory);
    }
}
