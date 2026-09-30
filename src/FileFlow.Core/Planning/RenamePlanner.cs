using System.Globalization;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Validation;

namespace FileFlow.Core.Planning;

/// <summary>
/// Builds validated rename previews (Before -> After) for prefix/suffix/replace/sequential
/// rules and for extension changes. Performs full validation before anything touches the disk:
/// name rules, duplicate targets inside the batch and collisions with existing files.
/// </summary>
public sealed class RenamePlanner
{
    private readonly IFileSystem _fileSystem;

    public RenamePlanner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>Plans a batch rename according to the rules. Entries never throw; problems are reported per entry.</summary>
    public RenamePlan Plan(IReadOnlyList<RenameInput> inputs, RenameRules rules)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(rules);

        var entries = new List<PlanEntry>(inputs.Count);
        var ruleError = ValidateRules(rules);

        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sequence = Math.Max(0, rules.SequentialStart);

        foreach (var input in inputs)
        {
            var currentName = System.IO.Path.GetFileName(input.FullPath);
            var targetPath = string.Empty;
            string? error = null;
            var errorKind = ErrorKind.None;

            if (ruleError is not null)
            {
                error = ruleError;
                errorKind = ErrorKind.PathValidation;
            }
            else if (string.IsNullOrEmpty(input.FullPath))
            {
                error = "Source path is empty.";
                errorKind = ErrorKind.PathValidation;
            }
            else
            {
                var newName = BuildName(currentName, rules, sequence);
                if (rules.UseSequentialNumbers)
                {
                    sequence++;
                }

                var nameError = FileNameValidator.Validate(newName);
                if (nameError is not null)
                {
                    error = nameError;
                    errorKind = ErrorKind.PathValidation;
                }
                else
                {
                    var directory = System.IO.Path.GetDirectoryName(input.FullPath) ?? string.Empty;
                    targetPath = System.IO.Path.Combine(directory, newName);

                    if (seenTargets.Contains(targetPath))
                    {
                        error = "Another file in this batch renames to the same target path.";
                        errorKind = ErrorKind.OperationConflict;
                    }
                    else
                    {
                        seenTargets.Add(targetPath);
                        if (!targetPath.Equals(input.FullPath, StringComparison.OrdinalIgnoreCase)
                            && _fileSystem.FileExists(targetPath))
                        {
                            error = "A file with the target name already exists in the folder.";
                            errorKind = ErrorKind.OperationConflict;
                        }
                    }
                }
            }

            entries.Add(new PlanEntry
            {
                FileId = input.FileId,
                SourcePath = input.FullPath,
                CurrentName = currentName,
                NewName = error is null ? System.IO.Path.GetFileName(targetPath) : currentName,
                TargetPath = targetPath,
                ErrorKind = errorKind,
                Error = error,
            });
        }

        return new RenamePlan
        {
            OperationName = TaskTypeNames.Rename,
            Entries = entries,
        };
    }

    /// <summary>Plans an extension-only change. <paramref name="newExtension"/> is "" to remove or ".xyz" style.</summary>
    public RenamePlan PlanExtensionChange(IReadOnlyList<RenameInput> inputs, string newExtension)
    {
        var entries = new List<PlanEntry>(inputs.Count);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var currentName = System.IO.Path.GetFileName(input.FullPath);
            string? error = null;
            var errorKind = ErrorKind.None;
            var targetPath = string.Empty;

            var extensionError = ValidateExtension(newExtension);
            if (extensionError is not null)
            {
                error = extensionError;
                errorKind = ErrorKind.PathValidation;
            }
            else if (string.IsNullOrEmpty(input.FullPath))
            {
                error = "Source path is empty.";
                errorKind = ErrorKind.PathValidation;
            }
            else
            {
                var (baseName, _) = SplitName(currentName);
                var newName = baseName + (newExtension.StartsWith('.') ? newExtension : string.Empty);
                var nameError = FileNameValidator.Validate(newName);
                if (nameError is not null)
                {
                    error = nameError;
                    errorKind = ErrorKind.PathValidation;
                }
                else
                {
                    var directory = System.IO.Path.GetDirectoryName(input.FullPath) ?? string.Empty;
                    targetPath = System.IO.Path.Combine(directory, newName);

                    if (seenTargets.Contains(targetPath))
                    {
                        error = "Another file in this batch would get the same target path.";
                        errorKind = ErrorKind.OperationConflict;
                    }
                    else
                    {
                        seenTargets.Add(targetPath);
                        if (!targetPath.Equals(input.FullPath, StringComparison.OrdinalIgnoreCase)
                            && _fileSystem.FileExists(targetPath))
                        {
                            error = "A file with the target name already exists in the folder.";
                            errorKind = ErrorKind.OperationConflict;
                        }
                    }
                }
            }

            entries.Add(new PlanEntry
            {
                FileId = input.FileId,
                SourcePath = input.FullPath,
                CurrentName = currentName,
                NewName = error is null ? System.IO.Path.GetFileName(targetPath) : currentName,
                TargetPath = targetPath,
                ErrorKind = errorKind,
                Error = error,
            });
        }

        return new RenamePlan
        {
            OperationName = TaskTypeNames.ChangeExtension,
            Entries = entries,
        };
    }

    private static string BuildName(string currentName, RenameRules rules, int sequence)
    {
        var (baseName, extension) = SplitName(currentName);

        if (!string.IsNullOrEmpty(rules.FindText))
        {
            baseName = ReplaceIgnoreCase(baseName, rules.FindText, rules.ReplaceText);
        }

        if (rules.UseSequentialNumbers)
        {
            var padding = Math.Clamp(rules.SequentialPadding, 1, 10);
            baseName += rules.SequentialSeparator
                        + sequence.ToString(new string('0', padding), CultureInfo.InvariantCulture);
        }

        return rules.Prefix + baseName + rules.Suffix + extension;
    }

    internal static (string BaseName, string Extension) SplitName(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        if (dotIndex <= 0)
        {
            return (fileName, string.Empty);
        }

        return (fileName[..dotIndex], fileName[dotIndex..]);
    }

    private static string ReplaceIgnoreCase(string source, string find, string replacement)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var found = source.IndexOf(find, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                result.Append(source, index, source.Length - index);
                break;
            }

            result.Append(source, index, found - index);
            result.Append(replacement);
            index = found + find.Length;
        }

        return result.ToString();
    }

    private static string? ValidateRules(RenameRules rules)
    {
        foreach (var (label, value) in new[]
                 {
                     ("Prefix", rules.Prefix),
                     ("Suffix", rules.Suffix),
                     ("Find text", rules.FindText),
                     ("Replace text", rules.ReplaceText),
                     ("Separator", rules.SequentialSeparator),
                 })
        {
            if (value.IndexOfAny(FileNameValidator.InvalidCharacters.ToArray()) >= 0)
            {
                return $"Rule {label} contains characters that are not allowed in file names.";
            }
        }

        if (rules.SequentialPadding is < 1 or > 10)
        {
            return "Sequential number padding must be between 1 and 10.";
        }

        return null;
    }

    private static string? ValidateExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null; // removing the extension is allowed
        }

        if (!extension.StartsWith('.'))
        {
            return "Extension must start with a dot, e.g. \".pdf\".";
        }

        var rest = extension[1..];
        if (rest.Length == 0)
        {
            return "Extension cannot be just a dot.";
        }

        if (rest.Contains('.'))
        {
            return "Only single-segment extensions are supported (e.g. \".pdf\").";
        }

        var probeError = FileNameValidator.Validate("a" + extension);
        if (probeError is not null)
        {
            return $"Invalid extension: {probeError}";
        }

        return null;
    }
}
