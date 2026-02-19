namespace TimeLogger.Infrastructure.Tempo;

public class TempoOptions
{
    public const string SectionName = "Tempo";

    /// <summary>Tempo API base URL, e.g. "https://api.tempo.io/4".</summary>
    public required string BaseUrl { get; set; }
}
