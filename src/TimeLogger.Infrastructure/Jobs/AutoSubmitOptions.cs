namespace TimeLogger.Infrastructure.Jobs;

public class AutoSubmitOptions
{
    public const string SectionName = "AutoSubmit";

    /// <summary>Master switch — the recurring job is only registered when true.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default: 08:00, 13:00 and 17:00 in <see cref="TimeZone"/>.</summary>
    public string Cron { get; set; } = "0 8,13,17 * * *";

    /// <summary>IANA time zone the cron and the "8 AM weekday" report rule are evaluated in.</summary>
    public string TimeZone { get; set; } = "Europe/Vilnius";

    /// <summary>
    /// Slack incoming webhook bound to the report channel (#timelog-alerts).
    /// Falls back to Notifications:SlackWebhookUrl when empty.
    /// </summary>
    public string? SlackWebhookUrl { get; set; }
}
