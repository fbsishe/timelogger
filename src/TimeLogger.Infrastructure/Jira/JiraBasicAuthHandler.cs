using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace TimeLogger.Infrastructure.Jira;

/// <summary>Injects Basic auth (email:token) into every Jira API request.</summary>
public class JiraBasicAuthHandler(IOptions<JiraOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.Value.Email}:{options.Value.ApiToken}"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        return base.SendAsync(request, cancellationToken);
    }
}
