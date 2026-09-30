using FileFlow.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FileFlow.Infrastructure.Logging;

/// <summary>
/// Rolling file logger writing one log file per day into the app data directory
/// (e.g. %LOCALAPPDATA%\FileFlow\logs\fileflow-20260930.log). The minimum level is
/// read from user settings at write time, so a settings change takes effect immediately.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDirectory;
    private readonly Func<string> _minimumLevel;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;
    private string _writerDate = string.Empty;
    private bool _disposed;

    public FileLoggerProvider(IAppPaths paths, Func<string> minimumLevel)
    {
        _logsDirectory = paths.LogsDirectory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(_logsDirectory);
        CleanupOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(LogLevel level, string categoryName, string message, Exception? exception)
    {
        if (_disposed || level < GetEffectiveLevel(categoryName))
        {
            return;
        }

        var timestamp = DateTime.Now;
        var line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                OpenWriter(timestamp);
                _writer!.WriteLine(line);
                _writer.Flush();
            }
            catch (IOException)
            {
                // Logging must never take the application down (e.g. disk full).
            }
            catch (UnauthorizedAccessException)
            {
                // same
            }
        }
    }

    private LogLevel GetEffectiveLevel(string categoryName)
    {
        var configured = ParseLevel(_minimumLevel());
        if (categoryName.StartsWith("Microsoft", StringComparison.Ordinal)
            && configured > LogLevel.Warning
            && configured < LogLevel.Error)
        {
            // Reduce framework noise unless the user explicitly wants verbose logs.
            return LogLevel.Warning;
        }

        return configured;
    }

    private static LogLevel ParseLevel(string value) => value?.ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" or "none" => LogLevel.Critical,
        _ => LogLevel.Information,
    };

    private void OpenWriter(DateTime timestamp)
    {
        var date = timestamp.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        if (_writer is null || _writerDate != date)
        {
            _writer?.Dispose();
            _writerDate = date;
            var path = Path.Combine(_logsDirectory, $"fileflow-{date}.log");
            _writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
        }
    }

    private void CleanupOldLogs(int keepDays = 30)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.EnumerateFiles(_logsDirectory, "fileflow-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            provider.Write(logLevel, categoryName, formatter(state, exception), exception);
        }
    }
}
