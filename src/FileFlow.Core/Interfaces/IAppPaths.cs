namespace FileFlow.Core.Interfaces;

/// <summary>Resolves the application data locations. No direct file system work.</summary>
public interface IAppPaths
{
    /// <summary>Root data directory, e.g. %LOCALAPPDATA%\FileFlow.</summary>
    string DataDirectory { get; }

    string DatabasePath { get; }

    string LogsDirectory { get; }
}
