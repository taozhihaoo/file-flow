namespace FileFlow.Core.State;

/// <summary>
/// Defines which <see cref="TaskState"/> transitions are legal.
/// Terminal states: Completed, CompletedWithErrors, Failed, Cancelled.
/// Interrupted tasks (crash recovery) may be re-queued or cancelled.
/// </summary>
public static class TaskStateMachine
{
    private static readonly Dictionary<TaskState, IReadOnlySet<TaskState>> AllowedTransitions =
        new()
        {
            [TaskState.Queued] = new HashSet<TaskState> { TaskState.Running, TaskState.Cancelled },
            [TaskState.Running] = new HashSet<TaskState>
            {
                TaskState.Completed,
                TaskState.CompletedWithErrors,
                TaskState.Failed,
                TaskState.Cancelled,
            },
            [TaskState.Interrupted] = new HashSet<TaskState> { TaskState.Queued, TaskState.Cancelled },
            [TaskState.Completed] = new HashSet<TaskState>(),
            [TaskState.CompletedWithErrors] = new HashSet<TaskState>(),
            [TaskState.Failed] = new HashSet<TaskState>(),
            [TaskState.Cancelled] = new HashSet<TaskState>(),
        };

    public static bool CanTransition(TaskState from, TaskState to)
        => AllowedTransitions[from].Contains(to);

    /// <summary>Throws <see cref="InvalidOperationException"/> when the transition is not allowed.</summary>
    public static void EnsureTransition(TaskState from, TaskState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal task state transition: {from} -> {to}.");
        }
    }
}
