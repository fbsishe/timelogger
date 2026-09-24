namespace TimeLogger.Domain.Entities;

/// <summary>
/// One morning digest the auto-submit job has dealt with, covering the local days
/// <see cref="FromDay"/> to <see cref="ToDay"/>. The latest <see cref="ToDay"/> is the
/// watermark that decides whether a run still owes Slack a digest, so a missed 08:00 run
/// is caught up by the next one instead of losing that day's report.
/// </summary>
public class DigestReport
{
    public int Id { get; set; }
    public DateOnly FromDay { get; set; }
    public DateOnly ToDay { get; set; }
    public DateTimeOffset HandledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>False when a quiet weekend digest was deliberately suppressed rather than posted.</summary>
    public bool Posted { get; set; }
}
