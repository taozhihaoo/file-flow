namespace FileFlow.Core;

/// <summary>
/// Unified error type for all recoverable failures. Carries an <see cref="ErrorKind"/>
/// so the UI can translate it into a user-facing message while logs keep the technical detail.
/// </summary>
public class FileFlowException : Exception
{
    public ErrorKind Kind { get; }

    public FileFlowException(ErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public static FileFlowException FileNotFound(string path, Exception? inner = null)
        => new(ErrorKind.FileNotFound, $"File not found: {path}", inner);

    public static FileFlowException FileAccess(string path, Exception? inner = null)
        => new(ErrorKind.FileAccess, $"Cannot access file: {path}", inner);

    public static FileFlowException PathValidation(string message, Exception? inner = null)
        => new(ErrorKind.PathValidation, message, inner);

    public static FileFlowException Conflict(string message, Exception? inner = null)
        => new(ErrorKind.OperationConflict, message, inner);

    public static FileFlowException Persistence(string message, Exception? inner = null)
        => new(ErrorKind.Persistence, message, inner);
}
