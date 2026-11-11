using FileFlow.Core;
using FileFlow.Core.Planning;

namespace FileFlow.App.Localization;

/// <summary>
/// Maps domain enums and the stable operation names (stored in the database) onto
/// localized display text. The stored values never change, only what the user sees.
/// </summary>
public static class DisplayNames
{
    public static string TaskStateName(TaskState state) => Loc.Instance[state switch
    {
        TaskState.Queued => "State_Queued",
        TaskState.Running => "State_Running",
        TaskState.Completed => "State_Completed",
        TaskState.CompletedWithErrors => "State_CompletedWithErrors",
        TaskState.Failed => "State_Failed",
        TaskState.Cancelled => "State_Cancelled",
        TaskState.Interrupted => "State_Interrupted",
        _ => "State_Cancelled",
    }];

    public static string ItemStateName(TaskItemState state) => Loc.Instance[state switch
    {
        TaskItemState.Pending => "State_Pending",
        TaskItemState.Succeeded => "State_Succeeded",
        TaskItemState.Failed => "State_Failed",
        TaskItemState.Cancelled => "State_Cancelled",
        _ => "State_Pending",
    }];

    public static string TaskTypeName(TaskType type) => Operation(TaskTypeNames.FromTaskType(type));

    /// <summary>Localized label for the stable operation name used by tasks and history.</summary>
    public static string Operation(string? operationType) => Loc.Instance[operationType switch
    {
        TaskTypeNames.Scan => "Op_Scan",
        TaskTypeNames.Rename => "Op_Rename",
        TaskTypeNames.Move => "Op_Move",
        TaskTypeNames.Copy => "Op_Copy",
        TaskTypeNames.Delete => "Op_Delete",
        TaskTypeNames.ChangeExtension => "Op_ChangeExtension",
        TaskTypeNames.AddTag => "Op_AddTag",
        TaskTypeNames.RemoveTag => "Op_RemoveTag",
        TaskTypeNames.Import => "Op_Import",
        TaskTypeNames.Export => "Op_Export",
        _ => "Op_Scan",
    }];

    /// <summary>Localized language name shown in the settings drop-down (always in its own language).</summary>
    public static string Language(string code) => code switch
    {
        FileFlow.Core.Localization.AppLanguages.Chinese => "简体中文",
        _ => "English",
    };
}
