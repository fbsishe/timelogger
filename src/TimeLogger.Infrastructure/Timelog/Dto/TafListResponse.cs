using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

/// <summary>TimeLog API Format (TAF) list response wrapper.</summary>
public class TafListResponse<T>
{
    [JsonPropertyName("Data")]
    public List<T> Data { get; set; } = [];

    [JsonPropertyName("Paging")]
    public TafPaging? Paging { get; set; }
}

public class TafPaging
{
    [JsonPropertyName("Page")]
    public int Page { get; set; }

    [JsonPropertyName("PageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("TotalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("HasNextPage")]
    public bool HasNextPage { get; set; }
}
