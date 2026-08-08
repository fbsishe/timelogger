namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// Credentials for the Timelog Reporting API (SOAP/XML at /service.asmx), created in
/// Timelog System Administration → Reporting API settings. Unlike the REST API key,
/// the Reporting API can read time registrations for ALL employees.
/// </summary>
public class TimelogReportingOptions
{
    public const string SectionName = "TimelogReporting";

    /// <summary>Full service URL; when empty it is derived from Timelog:BaseUrl by replacing /api with /service.asmx.</summary>
    public string? ServiceUrl { get; set; }

    public string? SiteCode { get; set; }
    public string? ApiId { get; set; }
    public string? ApiPassword { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SiteCode)
        && !string.IsNullOrWhiteSpace(ApiId)
        && !string.IsNullOrWhiteSpace(ApiPassword);
}
