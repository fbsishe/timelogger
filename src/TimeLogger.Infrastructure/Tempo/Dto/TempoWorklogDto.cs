using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Tempo.Dto;

public class TempoWorklogDto
{
    [JsonPropertyName("tempoWorklogId")]
    public long TempoWorklogId { get; set; }

    [JsonPropertyName("issue")]
    public TempoIssueRef? Issue { get; set; }

    [JsonPropertyName("timeSpentSeconds")]
    public int TimeSpentSeconds { get; set; }

    [JsonPropertyName("billableSeconds")]
    public int BillableSeconds { get; set; }

    [JsonPropertyName("startDate")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public TempoAuthor? Author { get; set; }

    [JsonPropertyName("attributes")]
    public TempoAttributes? Attributes { get; set; }
}

public class TempoIssueRef
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("self")]
    public string? Self { get; set; }
}

public class TempoAuthor
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = "";

    [JsonPropertyName("self")]
    public string? Self { get; set; }
}

public class TempoAttributes
{
    [JsonPropertyName("values")]
    public List<TempoAttributeValue> Values { get; set; } = [];
}

public class TempoAttributeValue
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
