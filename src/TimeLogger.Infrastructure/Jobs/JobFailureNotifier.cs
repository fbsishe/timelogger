using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Services;

namespace TimeLogger.Infrastructure.Jobs;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Slack incoming-webhook URL. Empty disables Slack alerts.</summary>
    public string? SlackWebhookUrl { get; set; }

    /// <summary>Recipient for failure emails. Empty disables email alerts.</summary>
    public string? EmailTo { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFrom { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
}

/// <summary>
/// Sends job-failure alerts to a Slack webhook and/or an email recipient,
/// depending on which channels are configured. Unconfigured channels are skipped.
/// </summary>
public class JobFailureNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<JobFailureNotifier> logger) : IJobFailureNotifier
{
    public async Task NotifyAsync(string jobName, string errorMessage, CancellationToken ct = default)
    {
        var opts = options.Value;
        var subject = $"TimeLogger job failed: {jobName}";
        var body = $"Background job '{jobName}' failed at {DateTimeOffset.UtcNow:u}.\n\nError:\n{errorMessage}";

        if (!string.IsNullOrWhiteSpace(opts.SlackWebhookUrl))
            await SendSlackAsync(opts.SlackWebhookUrl, subject, errorMessage, ct);

        if (!string.IsNullOrWhiteSpace(opts.EmailTo) && !string.IsNullOrWhiteSpace(opts.SmtpHost))
            await SendEmailAsync(opts, subject, body, ct);
    }

    private async Task SendSlackAsync(string webhookUrl, string subject, string error, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Notifications");
            var payload = JsonSerializer.Serialize(new { text = $":rotating_light: *{subject}*\n```{error}```" });
            var response = await client.PostAsync(
                webhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Sent Slack failure alert for {Subject}", subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Slack alert");
        }
    }

    private async Task SendEmailAsync(NotificationOptions opts, string subject, string body, CancellationToken ct)
    {
        try
        {
            using var smtp = new SmtpClient(opts.SmtpHost, opts.SmtpPort);
            if (!string.IsNullOrWhiteSpace(opts.SmtpUser))
            {
                smtp.Credentials = new System.Net.NetworkCredential(opts.SmtpUser, opts.SmtpPassword);
                smtp.EnableSsl = true;
            }

            using var message = new MailMessage(
                opts.SmtpFrom ?? "timelogger@relyits.se",
                opts.EmailTo!,
                subject,
                body);

            await smtp.SendMailAsync(message, ct);
            logger.LogInformation("Sent email failure alert to {Recipient}", opts.EmailTo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email alert");
        }
    }
}
