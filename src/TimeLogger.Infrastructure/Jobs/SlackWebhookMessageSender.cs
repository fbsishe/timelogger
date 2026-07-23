using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Jobs;

/// <summary>
/// Sends messages to the AutoSubmit webhook, falling back to the
/// job-failure notification webhook when the former is not configured.
/// </summary>
public class SlackWebhookMessageSender(
    IHttpClientFactory httpClientFactory,
    IOptions<AutoSubmitOptions> autoSubmitOptions,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<SlackWebhookMessageSender> logger) : ISlackMessageSender
{
    public async Task<bool> SendAsync(string text, CancellationToken ct = default)
    {
        var webhookUrl = !string.IsNullOrWhiteSpace(autoSubmitOptions.Value.SlackWebhookUrl)
            ? autoSubmitOptions.Value.SlackWebhookUrl
            : notificationOptions.Value.SlackWebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("No Slack webhook configured (AutoSubmit:SlackWebhookUrl / Notifications:SlackWebhookUrl) — message not sent");
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient("Notifications");
            var payload = JsonSerializer.Serialize(new { text });
            var response = await client.PostAsync(
                webhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to post message to Slack webhook");
            return false;
        }
    }
}
