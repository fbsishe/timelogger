namespace TimeLogger.Infrastructure.Jobs;

public class AutoSubmitOptions
{
    public const string SectionName = "AutoSubmit";

    /// <summary>Master switch — the recurring job is only registered when true.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default: 08:00, 13:00 and 17:00 in <see cref="TimeZone"/>.</summary>
    public string Cron { get; set; } = "0 8,13,17 * * *";

    /// <summary>IANA time zone the cron and <see cref="DigestHour"/> are evaluated in.</summary>
    public string TimeZone { get; set; } = "Europe/Vilnius";

    /// <summary>
    /// Local hour from which the daily digest of the previous day's submissions is due. The
    /// first run at or after it posts the digest — so a missed 08:00 run is caught up at 13:00 —
    /// and every other run only posts on errors. Keep it at or before the last <see cref="Cron"/> slot.
    /// </summary>
    public int DigestHour { get; set; } = 8;

    /// <summary>
    /// Slack incoming webhook for the auto-submit digest and error alerts. A webhook is bound
    /// to one channel, so moving the alerts means creating a webhook in the new channel.
    /// Falls back to Notifications:SlackWebhookUrl when empty.
    /// </summary>
    public string? SlackWebhookUrl { get; set; }
}
