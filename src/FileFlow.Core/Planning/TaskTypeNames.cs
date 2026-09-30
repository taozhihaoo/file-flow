namespace FileFlow.Core.Planning;

/// <summary>Stable operation names used in tasks and history.</summary>
public static class TaskTypeNames
{
    public const string Rename = "Rename";
    public const string Move = "Move";
    public const string Copy = "Copy";
    public const string Delete = "Delete";
    public const string ChangeExtension = "ChangeExtension";
    public const string AddTag = "AddTag";
    public const string RemoveTag = "RemoveTag";
    public const string Scan = "Scan";
    public const string Import = "Import";
    public const string Export = "Export";

    public static string FromTaskType(TaskType type) => type switch
    {
        TaskType.Rename => Rename,
        TaskType.Move => Move,
        TaskType.Copy => Copy,
        TaskType.Delete => Delete,
        TaskType.ChangeExtension => ChangeExtension,
        TaskType.AddTag => AddTag,
        TaskType.RemoveTag => RemoveTag,
        _ => type.ToString(),
    };

    public static string ToDisplay(TaskType type) => type switch
    {
        TaskType.Rename => "Rename",
        TaskType.Move => "Move",
        TaskType.Copy => "Copy",
        TaskType.Delete => "Delete",
        TaskType.ChangeExtension => "Change extension",
        TaskType.AddTag => "Add tag",
        TaskType.RemoveTag => "Remove tag",
        _ => type.ToString(),
    };
}
