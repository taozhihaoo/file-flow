using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileFlow.Core;
using FileFlow.Core.Formatting;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;

namespace FileFlow.App.ViewModels;

/// <summary>A search result row in the Files page.</summary>
public partial class FileRowViewModel : ObservableObject
{
    public FileRowViewModel(FileListItem item)
    {
        File = item.File;
        tags = item.Tags;
    }

    public FileRecord File { get; }

    [ObservableProperty]
    private string tags = string.Empty;

    public string NameText => File.FileName;

    public string PathText => File.FullPath;

    public string ExtensionText => File.Extension;

    public string SizeText => ByteSizeFormatter.Format(File.SizeBytes);

    public string ModifiedText => File.ModifiedAtUtc == default
        ? string.Empty
        : File.ModifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ScannedText => File.ScannedAtUtc == default
        ? string.Empty
        : File.ScannedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public bool IsMissing => File.IsMissing;
}

/// <summary>A task row in the Tasks page (also used on the dashboard).</summary>
public partial class TaskRowViewModel : ObservableObject
{
    public TaskRowViewModel(FileTask task)
    {
        Task = task;
    }

    public FileTask Task { get; }

    public long Id => Task.Id;

    public string TypeText => TaskTypeNames.ToDisplay(Task.Type);

    public string DescriptionText => string.IsNullOrWhiteSpace(Task.Description) ? TypeText : Task.Description;

    public string StateText => TaskStateText(Task.State);

    public string CountsText => $"{Task.Succeeded:N0} ok · {Task.Failed:N0} failed · {Task.Total:N0} total";

    public double ProgressPercent => Task.Total == 0
        ? 0
        : Math.Round(Task.Processed * 100.0 / Task.Total, 1);

    public string CreatedText => Task.CreatedAtUtc == default
        ? string.Empty
        : Task.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string CompletedText => Task.CompletedAtUtc is null
        ? string.Empty
        : Task.CompletedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public bool CanCancel => Task.State is TaskState.Queued or TaskState.Running;

    public bool CanRetry => Task.Failed > 0;

    public void RefreshState()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(CompletedText));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
    }

    public static string TaskStateText(TaskState state) => state switch
    {
        TaskState.Queued => "Queued",
        TaskState.Running => "Running",
        TaskState.Completed => "Completed",
        TaskState.CompletedWithErrors => "Completed with errors",
        TaskState.Failed => "Failed",
        TaskState.Cancelled => "Cancelled",
        TaskState.Interrupted => "Interrupted",
        _ => state.ToString(),
    };
}

/// <summary>An item row of a selected task.</summary>
public sealed class TaskItemRowViewModel
{
    public TaskItemRowViewModel(TaskItemRecord item)
    {
        Item = item;
    }

    public TaskItemRecord Item { get; }

    public string StateText => Item.State.ToString();

    public string SourcePath => Item.SourcePath;

    public string TargetPath => Item.TargetPath ?? string.Empty;

    public string ErrorText => string.IsNullOrEmpty(Item.ErrorMessage) ? string.Empty : Item.ErrorMessage;

    public string ProcessedText => Item.ProcessedAtUtc is null
        ? string.Empty
        : Item.ProcessedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss");

    public bool IsFailed => Item.State == TaskItemState.Failed;
}

/// <summary>An operation history row (Tasks page, History page and Dashboard).</summary>
public sealed class OperationRowViewModel
{
    public OperationRowViewModel(OperationRecord record)
    {
        Record = record;
    }

    public OperationRecord Record { get; }

    public string TypeText => Record.OperationType switch
    {
        TaskTypeNames.ChangeExtension => "Change extension",
        TaskTypeNames.AddTag => "Add tag",
        TaskTypeNames.RemoveTag => "Remove tag",
        _ => Record.OperationType,
    };

    public string DescriptionText => Record.Description ?? string.Empty;

    public string CountsText => $"{Record.SuccessCount:N0} ok · {Record.FailureCount:N0} failed · {Record.ItemCount:N0} total";

    public bool HasFailures => Record.FailureCount > 0;

    public string WhenText => Record.CreatedAtUtc == default
        ? string.Empty
        : Record.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>Page view models can refresh their data when navigated to.</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}

/// <summary>Receives DataGrid multi-selection updates routed through the selection behavior.</summary>
public interface ISelectionConsumer
{
    void OnSelectionChanged();
}
