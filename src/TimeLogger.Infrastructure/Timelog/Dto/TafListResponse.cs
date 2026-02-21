using System.Text.Json.Serialization;

namespace TimeLogger.Infrastructure.Timelog.Dto;

/// <summary>
/// TimeLog HAL-style list response.
/// Items are nested as Entities[i].Properties.
/// </summary>
public class TafListResponse<T>
{
    [JsonPropertyName("Entities")]
    public List<TafEntity<T>> Entities { get; set; } = [];

    [JsonPropertyName("Properties")]
    public TafListProperties? Properties { get; set; }

    /// <summary>Convenience accessor — flattens Entities[i].Properties into a plain list.</summary>
    public List<T> Data => Entities.ConvertAll(e => e.Properties);
}

public class TafEntity<T>
{
    [JsonPropertyName("Properties")]
    public T Properties { get; set; } = default!;
}

public class TafListProperties
{
    [JsonPropertyName("TotalRecord")]
    public string TotalRecord { get; set; } = "0";

    [JsonPropertyName("TotalPage")]
    public string TotalPage { get; set; } = "0";

    [JsonPropertyName("PageNumber")]
    public string PageNumber { get; set; } = "1";
}
