namespace FileFlow.Core.Models;

/// <summary>Aggregated numbers shown on the dashboard.</summary>
public sealed record DashboardStats(
    int FilesIndexed,
    long TotalSizeBytes,
    int MissingFiles,
    int TagCount,
    int QueuedTasks,
    int RunningTasks,
    int FailedTasks,
    IReadOnlyList<FileTask> RecentTasks,
    IReadOnlyList<OperationRecord> RecentScans);
