using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TimeLogger.Infrastructure.Timelog;

public class TimelogReportingClient(
    IHttpClientFactory httpClientFactory,
    IOptions<TimelogReportingOptions> reportingOptions,
    IOptions<TimelogOptions> timelogOptions,
    ILogger<TimelogReportingClient> logger) : ITimelogReportingClient
{
    public const string HttpClientName = "TimelogReporting";

    private readonly TimelogReportingOptions _options = reportingOptions.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<WorkUnitItem>> GetWorkUnitsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Timelog Reporting API credentials are not configured.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{ResolveServiceUrl()}/GetWorkUnitsRawPaged";
        var results = new List<WorkUnitItem>();
        var page = 1;
        int totalPages;

        do
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["siteCode"] = _options.SiteCode!,
                ["apiID"] = _options.ApiId!,
                ["apiPassword"] = _options.ApiPassword!,
                ["workUnitID"] = "0",
                ["employeeID"] = "0",
                ["allocationID"] = "0",
                ["taskID"] = "0",
                ["projectID"] = "0",
                ["departmentID"] = "0",
                ["startDate"] = from.ToString("yyyy-MM-dd"),
                ["endDate"] = to.ToString("yyyy-MM-dd"),
                ["page"] = page.ToString(),
                ["pageSize"] = "500",
            });

            var response = await client.PostAsync(url, form, ct);
            var xml = await response.Content.ReadAsStringAsync(ct);
            response.EnsureSuccessStatusCode();

            var root = XDocument.Parse(xml).Root
                ?? throw new InvalidOperationException("Empty Reporting API response.");

            if (root.Name.LocalName == "Errors")
            {
                var message = string.Join("; ", root.Elements().Select(e => e.Value));
                throw new InvalidOperationException($"Timelog Reporting API error: {message}");
            }

            totalPages = (int?)root.Attribute("TotalPages") ?? 1;
            results.AddRange(root.Elements().Where(e => e.Name.LocalName == "WorkUnit").Select(ParseWorkUnit));
            page++;
        } while (page <= totalPages);

        logger.LogDebug("Reporting API returned {Count} work units for {From}..{To}", results.Count, from, to);
        return results;
    }

    private string ResolveServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
            return _options.ServiceUrl.TrimEnd('/');

        // https://app2.timelog.com/relyits/api → https://app2.timelog.com/relyits/service.asmx
        var baseUrl = timelogOptions.Value.BaseUrl?.TrimEnd('/')
            ?? throw new InvalidOperationException("Neither TimelogReporting:ServiceUrl nor Timelog:BaseUrl is configured.");
        return baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? baseUrl[..^4] + "/service.asmx"
            : baseUrl + "/service.asmx";
    }

    private static WorkUnitItem ParseWorkUnit(XElement unit)
    {
        string? Get(string name) =>
            unit.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        double GetDouble(string name) =>
            double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        int GetInt(string name) =>
            int.TryParse(Get(name), out var i) ? i : 0;

        DateTime? GetDate(string name) =>
            DateTime.TryParse(Get(name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        return new WorkUnitItem(
            TimeRegistrationGuid: Get("TimeRegistrationGuid"),
            UserId: GetInt("UserID"),
            TaskId: GetInt("TaskID"),
            TaskName: Get("TaskName"),
            ProjectName: Get("ProjectName"),
            Date: DateOnly.FromDateTime(GetDate("Date") ?? default),
            Note: Get("Note"),
            Hours: GetDouble("RegHours"),
            ApprovedStatus: GetInt("ApprovedStatus"),
            Invoiced: GetInt("InvoiceStatus") != 0,
            Created: GetDate("CreatedAt"),
            LastModified: GetDate("LastModifiedAt"));
    }
}
