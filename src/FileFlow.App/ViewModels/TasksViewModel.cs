using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;
using FileFlow.App.Services;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>Tasks page: queue overview, per-task items, cancel/retry/clear actions.</summary>
public partial class TasksViewModel : ObservableObject, IRefreshable
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskQueueService _queue;
    private readonly IDialogService _dialogs;
    private readonly ILogger<TasksViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private DispatcherOperation? _pendingRefresh;

    public TasksViewModel(
        ITaskRepository tasks,
        ITaskQueueService queue,
        IDialogService dialogs,
        ILogger<TasksViewModel> logger)
    {
        _tasks = tasks;
        _queue = queue;
        _dialogs = dialogs;
        _logger = logger;
        _queue.TaskChanged += OnTaskChanged;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = new();

    public ObservableCollection<TaskItemRowViewModel> SelectedTaskItems { get; } = new();

    [ObservableProperty]
    private TaskRowViewModel? selectedTask;

    [ObservableProperty]
    private int finishedCount;

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        _ = LoadItemsAsync(value);
    }

    private void OnTaskChanged(object? sender, TaskEventArgs e)
    {
        // The queue raises on worker threads; debounce and marshal to the UI thread.
        if (_pendingRefresh is not null)
        {
            return;
        }

        _pendingRefresh = _dispatcher.BeginInvoke(async () =>
        {
            _pendingRefresh = null;
            await RefreshAsync();
        });
    }

    public async Task RefreshAsync()
    {
        try
        {
            var tasks = await _tasks.GetRecentAsync(100);
            var selectedId = SelectedTask?.Id;

            Tasks.Clear();
            foreach (var task in tasks)
            {
                Tasks.Add(new TaskRowViewModel(task));
            }

            FinishedCount = tasks.Count(t => t.State
                is TaskState.Completed
                or TaskState.CompletedWithErrors
                or TaskState.Failed
                or TaskState.Cancelled
                or TaskState.Interrupted);

            SelectedTask = Tasks.FirstOrDefault(t => t.Id == selectedId) ?? Tasks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh the task list");
        }
    }

    private async Task LoadItemsAsync(TaskRowViewModel? row)
    {
        SelectedTaskItems.Clear();
        if (row is null)
        {
            return;
        }

        try
        {
            var items = await _tasks.GetItemsAsync(row.Id);
            foreach (var item in items.Take(2000))
            {
                SelectedTaskItems.Add(new TaskItemRowViewModel(item));
            }

            // Show live progress while the selected task is running.
            if (row.Task.State is TaskState.Running or TaskState.Queued)
            {
                ScheduleLiveItemRefresh(row.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load items of task {TaskId}", row.Id);
        }
    }

    private async void ScheduleLiveItemRefresh(long taskId)
    {
        await Task.Delay(1000);
        if (SelectedTask?.Id == taskId && SelectedTask.Task.State is TaskState.Running or TaskState.Queued)
        {
            await LoadItemsAsync(SelectedTask);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        var task = SelectedTask;
        if (task is null || !task.CanCancel)
        {
            return;
        }

        if (task.Task.State is TaskState.Running
            && !_dialogs.Confirm(
                Loc.Instance["Tasks_CancelTitle"],
                Loc.Instance.F("Tasks_CancelMessage", task.DescriptionText),
                Loc.Instance["Tasks_CancelDetails"]))
        {
            return;
        }

        try
        {
            await _queue.CancelTaskAsync(task.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not cancel task {TaskId}", task.Id);
            _dialogs.ShowError(Loc.Instance["Tasks_CancelTitle"], Loc.Instance.F("Tasks_CancelError", ex.Message));
        }
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        var task = SelectedTask;
        if (task is null || !task.CanRetry)
        {
            return;
        }

        try
        {
            var newTaskId = await _queue.RetryFailedItemsAsync(task.Id);
            _dialogs.ShowInfo(Loc.Instance["Tasks_RetryTitle"], Loc.Instance.F("Tasks_RetryQueued", newTaskId));
            await RefreshAsync();
        }
        catch (FileFlow.Core.FileFlowException ex)
        {
            _dialogs.ShowError(Loc.Instance["Tasks_RetryErrorTitle"], ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not retry task {TaskId}", task.Id);
            _dialogs.ShowError(Loc.Instance["Tasks_RetryErrorTitle"], Loc.Instance.F("Tasks_RetryUnexpected", ex.Message));
        }
    }

    [RelayCommand]
    private async Task ClearFinishedAsync()
    {
        if (FinishedCount == 0)
        {
            return;
        }

        if (!_dialogs.Confirm(
                Loc.Instance["Tasks_ClearTitle"],
                Loc.Instance.F("Tasks_ClearMessage", FinishedCount),
                Loc.Instance["Tasks_ClearDetails"]))
        {
            return;
        }

        try
        {
            await _queue.ClearFinishedTasksAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not clear finished tasks");
            _dialogs.ShowError(Loc.Instance["Tasks_ClearTitle"], Loc.Instance.F("Tasks_ClearError", ex.Message));
        }
    }
}
