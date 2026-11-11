namespace FileFlow.App.ViewModels;

/// <summary>A filter entry shown in a drop-down. <see cref="Value"/> "" means "all".</summary>
public sealed record FilterOption(string Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>A tag filter entry; <see cref="TagId"/> null means "all tags".</summary>
public sealed record TagFilterOption(long? TagId, string Display)
{
    public override string ToString() => Display;
}
