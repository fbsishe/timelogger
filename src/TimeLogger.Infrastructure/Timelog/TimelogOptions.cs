namespace TimeLogger.Infrastructure.Timelog;

public class TimelogOptions
{
    public const string SectionName = "Timelog";

    public required string BaseUrl { get; set; }
    public required string ApiKey { get; set; }

    /// <summary>Max submission attempts per entry for transient API errors (network, 5xx, 429).</summary>
    public int SubmitRetryCount { get; set; } = 3;

    /// <summary>Base delay between retries; doubles per attempt (2s, 4s, 8s, …).</summary>
    public double SubmitRetryBaseDelaySeconds { get; set; } = 2;
}
