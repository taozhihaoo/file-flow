using FileFlow.Core.Interfaces;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Planning;

/// <summary>A file selected for a move or copy operation.</summary>
public sealed record TransferInput(long FileId, string FullPath);

/// <summary>Validated preview of a move or copy operation.</summary>
public sealed class TransferPlan
{
    public required bool IsCopy { get; init; }

    public required string TargetDirectory { get; init; }

    public required IReadOnlyList<PlanEntry> Entries { get; init; }

    public int ValidCount => Entries.Count(e => e.IsValid);

    public int ErrorCount => Entries.Count - ValidCount;

    public string OperationName => IsCopy ? TaskTypeNames.Copy : TaskTypeNames.Move;
}

/// <summary>
/// Builds validated move/copy previews: target directory checks, name collisions
/// inside the batch and existing-file conflicts are detected before execution.
/// </summary>
public sealed class TransferPlanner
{
    private readonly IFileSystem _fileSystem;

    public TransferPlanner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public TransferPlan Plan(IReadOnlyList<TransferInput> inputs, string targetDirectory, bool isCopy)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var entries = new List<PlanEntry>(inputs.Count);
        var directoryError = PathValidator.ValidateDirectoryPath(targetDirectory);
        var directoryExists = directoryError is null && _fileSystem.DirectoryExists(targetDirectory);

        if (directoryError is null && !directoryExists)
        {
            directoryError = $"Target folder does not exist: {targetDirectory}";
        }

        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var currentName = System.IO.Path.GetFileName(input.FullPath);
            string? error = directoryError;
            var errorKind = directoryError is not null ? ErrorKind.PathValidation : ErrorKind.None;
            var targetPath = string.Empty;

            if (error is null && string.IsNullOrEmpty(input.FullPath))
            {
                error = "Source path is empty.";
                errorKind = ErrorKind.PathValidation;
            }

            if (error is null)
            {
                targetPath = System.IO.Path.Combine(targetDirectory, currentName);

                if (!isCopy && targetPath.Equals(input.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Source and target are the same file.";
                    errorKind = ErrorKind.OperationConflict;
                }
                else if (seenTargets.Contains(targetPath))
                {
                    error = isCopy
                        ? "Two files in this selection copy to the same target path."
                        : "Two files in this selection move to the same target path.";
                    errorKind = ErrorKind.OperationConflict;
                }
                else if (_fileSystem.FileExists(targetPath))
                {
                    error = "A file with the same name already exists in the target folder.";
                    errorKind = ErrorKind.OperationConflict;
                }
                else
                {
                    seenTargets.Add(targetPath);
                }
            }

            entries.Add(new PlanEntry
            {
                FileId = input.FileId,
                SourcePath = input.FullPath,
                CurrentName = currentName,
                NewName = currentName,
                TargetPath = targetPath,
                ErrorKind = errorKind,
                Error = error,
            });
        }

        return new TransferPlan
        {
            IsCopy = isCopy,
            TargetDirectory = targetDirectory,
            Entries = entries,
        };
    }
}
