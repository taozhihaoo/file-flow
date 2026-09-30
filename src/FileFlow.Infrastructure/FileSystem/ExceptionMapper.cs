using FileFlow.Core;

namespace FileFlow.Infrastructure.FileSystem;

/// <summary>Translates raw file system exceptions into classified <see cref="FileFlowException"/>s.</summary>
public static class ExceptionMapper
{
    private const int HResultSharingViolation = 32;   // ERROR_SHARING_VIOLATION: file is locked
    private const int HResultDiskFull = 70;           // ERROR_DISK_FULL
    private const int HResultDiskFull2 = 112;         // ERROR_DISK_FULL variant
    private const int HResultFileExists = 80;         // ERROR_FILE_EXISTS
    private const int HResultAlreadyExists = 183;     // ERROR_ALREADY_EXISTS

    /// <summary>Maps any exception to a <see cref="FileFlowException"/> with a stable <see cref="ErrorKind"/>.</summary>
    public static FileFlowException Map(Exception exception, string path) => exception switch
    {
        FileFlowException mapped => mapped,
        OperationCanceledException => new FileFlowException(ErrorKind.Cancelled, $"Operation cancelled: {path}", exception),
        FileNotFoundException => FileFlowException.FileNotFound(path, exception),
        DirectoryNotFoundException => FileFlowException.FileNotFound(path, exception),
        UnauthorizedAccessException => FileFlowException.FileAccess(path, exception),
        System.IO.PathTooLongException => FileFlowException.PathValidation($"Path is too long: {path}", exception),
        ArgumentException => FileFlowException.PathValidation($"Invalid path: {path}", exception),
        NotSupportedException => FileFlowException.PathValidation($"Path format is not supported: {path}", exception),
        IOException io when ((uint)io.HResult & 0xFFFF) is HResultFileExists or HResultAlreadyExists
            => FileFlowException.Conflict($"Target already exists: {path}", exception),
        IOException io when ((uint)io.HResult & 0xFFFF) == HResultSharingViolation
            => FileFlowException.FileAccess(path, exception),
        IOException io when ((uint)io.HResult & 0xFFFF) is HResultDiskFull or HResultDiskFull2
            => new FileFlowException(ErrorKind.FileAccess, "The disk is full.", exception),
        IOException => FileFlowException.FileAccess(path, exception),
        System.ComponentModel.Win32Exception => FileFlowException.FileAccess(path, exception),
        _ => new FileFlowException(ErrorKind.Unknown, $"Unexpected error while accessing {path}: {exception.Message}", exception),
    };

    /// <summary>Runs an action and maps exceptions to <see cref="FileFlowException"/>.</summary>
    public static void Run(Action action, string path)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw Map(ex, path);
        }
    }
}
