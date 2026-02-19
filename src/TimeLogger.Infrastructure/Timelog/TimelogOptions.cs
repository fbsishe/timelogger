namespace TimeLogger.Infrastructure.Timelog;

public class TimelogOptions
{
    public const string SectionName = "Timelog";

    public required string BaseUrl { get; set; }
    public required string ApiKey { get; set; }
}
