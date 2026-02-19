namespace TimeLogger.Infrastructure.Jira;

public class JiraOptions
{
    public const string SectionName = "Jira";

    /// <summary>Jira Cloud base URL, e.g. "https://yourcompany.atlassian.net".</summary>
    public required string BaseUrl { get; set; }

    /// <summary>Jira user email used for Basic auth.</summary>
    public required string Email { get; set; }

    /// <summary>Jira API token (generated at id.atlassian.com).</summary>
    public required string ApiToken { get; set; }
}
