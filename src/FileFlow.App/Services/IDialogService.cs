using FileFlow.Core.Models;

namespace FileFlow.App.Services;

public sealed record TagPickResult(string TagName, bool CreateNew);

/// <summary>
/// Abstraction over all dialogs so view models stay testable and free of view types.
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string? details = null);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    /// <summary>Folder picker. Returns null when the user cancels.</summary>
    string? PickFolder(string? initialPath, string? title = null);

    string? PickSaveCsvFile(string defaultFileName, string? initialDirectory);

    string? PickOpenCsvFile();

    /// <summary>Batch preview (rename/move/copy/delete). Returns true when the user confirmed execution.</summary>
    bool ShowBatchPreview(ViewModels.BatchPreviewViewModel viewModel);

    /// <summary>Single-line text prompt. Returns null when the user cancels.</summary>
    string? PromptText(string title, string label, string? defaultValue = null, Func<string, string?>? validate = null);

    /// <summary>Picks an existing tag or creates a new one. Returns null when the user cancels.</summary>
    TagPickResult? PickTag(IReadOnlyList<Tag> existingTags, bool allowCreate);
}
