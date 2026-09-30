namespace FileFlow.Core.Models;

/// <summary>
/// File library search criteria. Null/empty fields are ignored.
/// </summary>
public sealed class SearchFilter
{
    /// <summary>Free-text keyword matched against file name and path.</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Exact extension match including the dot, e.g. ".pdf". Case-insensitive.</summary>
    public string? Extension { get; set; }

    public long? MinSizeBytes { get; set; }

    public long? MaxSizeBytes { get; set; }

    public DateTime? ModifiedFromUtc { get; set; }

    public DateTime? ModifiedToUtc { get; set; }

    /// <summary>Files must carry all of these tags.</summary>
    public IReadOnlyList<long> TagIds { get; set; } = Array.Empty<long>();

    /// <summary>null = include everything; true = only missing; false = only present.</summary>
    public bool? Missing { get; set; }

    /// <summary>Restrict results to files under this root path (prefix match).</summary>
    public string? RootPath { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; } = 500;

    /// <summary>True when no criterion is set (the query would return everything).</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Keyword)
        && Extension is null
        && MinSizeBytes is null
        && MaxSizeBytes is null
        && ModifiedFromUtc is null
        && ModifiedToUtc is null
        && TagIds.Count == 0
        && Missing is null
        && RootPath is null;
}
