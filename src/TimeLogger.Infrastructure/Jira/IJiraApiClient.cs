using Refit;
using TimeLogger.Infrastructure.Jira.Dto;

namespace TimeLogger.Infrastructure.Jira;

/// <summary>Jira Cloud REST API client for enriching worklogs with issue metadata.</summary>
public interface IJiraApiClient
{
    /// <summary>Returns a Jira issue with all fields, including custom fields.</summary>
    [Get("/rest/api/3/issue/{issueId}")]
    Task<JiraIssueDto> GetIssueAsync(
        long issueId,
        [Query] string? fields = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns basic user info (displayName, emailAddress) for an Atlassian account ID.</summary>
    [Get("/rest/api/3/user")]
    Task<JiraUserDto> GetUserAsync(
        [Query] string accountId,
        CancellationToken cancellationToken = default);
}
