using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace TimeLogger.Infrastructure.Timelog;

/// <summary>
/// DelegatingHandler that injects the Timelog PAT as a Bearer token on every outbound request.
/// </summary>
public class BearerTokenHandler(IOptions<TimelogOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);

        return base.SendAsync(request, cancellationToken);
    }
}
