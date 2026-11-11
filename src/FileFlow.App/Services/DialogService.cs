using System.Windows;
using Microsoft.Win32;
using FileFlow.App.Localization;
using FileFlow.Core.Models;
using FileFlow.App.Dialogs;
using FileFlow.App.ViewModels;

namespace FileFlow.App.Services;

/// <summary>WPF implementation of the dialog abstraction. All dialogs are owned by the main window.</summary>
public sealed class DialogService : IDialogService
{
    private static Window? OwnerWindow => Application.Current?.MainWindow;

    public bool Confirm(string title, string message, string? details = null)
    {
        var viewModel = new ConfirmDialogViewModel
        {
            Title = title,
            Message = message,
            Details = details,
            ConfirmText = Loc.Instance["Common_OK"],
        };
        var dialog = new ConfirmDialog(viewModel) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(OwnerWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(OwnerWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public string? PickFolder(string? initialPath, string? title = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title ?? Loc.Instance["Common_Browse"],
        };
        if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog(OwnerWindow) == true ? dialog.FolderName : null;
    }

    public string? PickSaveCsvFile(string defaultFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.Instance["Files_Export"],
            FileName = defaultFileName,
            Filter = "CSV (*.csv)|*.csv|*.*|*.*",
            DefaultExt = "csv",
        };
        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(OwnerWindow) == true ? dialog.FileName : null;
    }

    public string? PickOpenCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Instance["Files_Import"],
            Filter = "CSV (*.csv)|*.csv|*.*|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(OwnerWindow) == true ? dialog.FileName : null;
    }

    public bool ShowBatchPreview(BatchPreviewViewModel viewModel)
    {
        var dialog = new BatchPreviewWindow(viewModel) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public string? PromptText(string title, string label, string? defaultValue = null, Func<string, string?>? validate = null)
    {
        var viewModel = new TextPromptViewModel(title, label, defaultValue, validate);
        var dialog = new TextPromptDialog(viewModel) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? viewModel.Value : null;
    }

    public TagPickResult? PickTag(IReadOnlyList<Tag> existingTags, bool allowCreate)
    {
        var viewModel = new TagPickerViewModel
        {
            Title = "Tags",
            Label = allowCreate
                ? "Choose an existing tag or create a new one."
                : "Choose the tag to remove.",
            ConfirmText = Loc.Instance["Common_OK"],
            AllowCreate = allowCreate,
            Tags = existingTags,
        };
        TagPickResult? confirmed = null;
        viewModel.Confirmed += result => confirmed = result;
        var dialog = new TagPickerDialog(viewModel) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? confirmed : null;
    }
}
