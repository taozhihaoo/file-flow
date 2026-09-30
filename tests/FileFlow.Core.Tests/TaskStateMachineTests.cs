using Xunit;
using FileFlow.Core;
using FileFlow.Core.State;

namespace FileFlow.Core.Tests;

public class TaskStateMachineTests
{
    public static TheoryData<TaskState, TaskState> ValidTransitions => new()
    {
        { TaskState.Queued, TaskState.Running },
        { TaskState.Queued, TaskState.Cancelled },
        { TaskState.Running, TaskState.Completed },
        { TaskState.Running, TaskState.CompletedWithErrors },
        { TaskState.Running, TaskState.Failed },
        { TaskState.Running, TaskState.Cancelled },
        { TaskState.Interrupted, TaskState.Queued },
        { TaskState.Interrupted, TaskState.Cancelled },
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransition_AcceptsLegalTransitions(TaskState from, TaskState to)
    {
        Assert.True(TaskStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TaskState.Completed, TaskState.Running)]
    [InlineData(TaskState.Completed, TaskState.Cancelled)]
    [InlineData(TaskState.Failed, TaskState.Running)]
    [InlineData(TaskState.Cancelled, TaskState.Running)]
    [InlineData(TaskState.Cancelled, TaskState.Queued)]
    [InlineData(TaskState.CompletedWithErrors, TaskState.Running)]
    [InlineData(TaskState.Queued, TaskState.Completed)]
    [InlineData(TaskState.Queued, TaskState.Queued)]
    public void CanTransition_RejectsIllegalTransitions(TaskState from, TaskState to)
    {
        Assert.False(TaskStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void EnsureTransition_ThrowsOnIllegalTransition()
    {
        Assert.Throws<InvalidOperationException>(() => TaskStateMachine.EnsureTransition(TaskState.Completed, TaskState.Running));
    }

    [Fact]
    public void EnsureTransition_DoesNotThrowOnLegalTransition()
    {
        TaskStateMachine.EnsureTransition(TaskState.Running, TaskState.CompletedWithErrors);
    }

    [Fact]
    public void TerminalStates_CannotLeave()
    {
        foreach (var terminal in new[] { TaskState.Completed, TaskState.CompletedWithErrors, TaskState.Failed, TaskState.Cancelled })
        {
            foreach (var target in Enum.GetValues<TaskState>())
            {
                if (target != terminal)
                {
                    Assert.False(TaskStateMachine.CanTransition(terminal, target), $"{terminal} -> {target} should be illegal");
                }
            }
        }
    }
}
