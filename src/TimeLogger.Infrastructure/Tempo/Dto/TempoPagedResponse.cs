using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Tempo.Dto;

public class TempoPagedResponse<T>
{
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = [];

    [JsonPropertyName("metadata")]
    public TempoMetadata? Metadata { get; set; }
}

public class TempoMetadata
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}
