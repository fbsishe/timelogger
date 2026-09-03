namespace TimeLogger.Infrastructure.Tempo;

public class TempoOptions
{
    public const string SectionName = "Tempo";

    /// <summary>Tempo API base URL, e.g. "https://api.tempo.io/4".</summary>
    public required string BaseUrl { get; set; }

    /// <summary>
    /// How far back the incremental pull looks for <em>work dates</em>. Tempo applies
    /// <c>from</c>/<c>to</c> alongside <c>updatedFrom</c>, so this bounds how old a
    /// back-dated worklog may be and still be picked up.
    /// </summary>
    public int LookbackDays { get; set; } = 90;

    /// <summary>
    /// Safety overlap subtracted from the stored watermark, to cover clock skew between
    /// us and Tempo and worklogs amended while a pull was in flight.
    /// </summary>
    public int WatermarkOverlapMinutes { get; set; } = 120;
}
