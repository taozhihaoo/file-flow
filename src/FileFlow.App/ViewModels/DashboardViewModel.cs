using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileFlow.Core;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>Dashboard: library totals, task activity and recent scans.</summary>
public partial class DashboardViewModel : ObservableObject, IRefreshable
{
    private readonly IFileRepository _files;
    private readonly ITagRepository _tags;
    private readonly ITaskRepository _tasks;
    private readonly IHistoryRepository _history;
    private readonly ILogger<DashboardViewModel> _logger;

    public DashboardViewModel(
        IFileRepository files,
        ITagRepository tags,
        ITaskRepository tasks,
        IHistoryRepository history,
        ILogger<DashboardViewModel> logger)
    {
        _files = files;
        _tags = tags;
        _tasks = tasks;
        _history = history;
        _logger = logger;
    }

    [ObservableProperty]
    private int filesIndexed;

    [ObservableProperty]
    private string totalSizeText = "0 B";

    [ObservableProperty]
    private int missingFiles;

    [ObservableProperty]
    private int tagCount;

    [ObservableProperty]
    private int activeTasks;

    [ObservableProperty]
    private int failedOperations;

    [ObservableProperty]
    private bool isEmpty;

    public ObservableCollection<TaskRowViewModel> RecentTasks { get; } = new();

    public ObservableCollection<OperationRowViewModel> RecentScans { get; } = new();

    public async Task RefreshAsync()
    {
        try
        {
            var (files, size, missing, tags, recentTasks, scans, failedOps) = await LoadAsync();
            FilesIndexed = files;
            TotalSizeText = FileFlow.Core.Formatting.ByteSizeFormatter.Format(size);
            MissingFiles = missing;
            TagCount = tags;
            ActiveTasks = recentTasks.Count(t => t.State is TaskState.Queued or TaskState.Running);
            FailedOperations = failedOps;
            IsEmpty = files == 0;

            RecentTasks.Clear();
            foreach (var task in recentTasks.Take(5))
            {
                RecentTasks.Add(new TaskRowViewModel(task));
            }

            RecentScans.Clear();
            foreach (var scan in scans)
            {
                RecentScans.Add(new OperationRowViewModel(scan));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load dashboard statistics");
        }
    }

    private async Task<(int Files, long Size, int Missing, int Tags, IReadOnlyList<FileTask> RecentTasks, IReadOnlyList<OperationRecord> Scans, int FailedOps)> LoadAsync()
    {
        var files = await _files.CountAllAsync();
        var size = await _files.SumSizeAsync();
        var missing = await _files.CountMissingAsync();
        var tags = await _tags.GetAllAsync();
        var recentTasks = await _tasks.GetRecentAsync(10);
        var scans = await _history.GetByTypeAsync(TaskTypeNames.Scan, 5);
        var failedOps = await _history.CountFailedOperationsAsync();
        return (files, size, missing, tags.Count, recentTasks, scans, failedOps);
    }
}
