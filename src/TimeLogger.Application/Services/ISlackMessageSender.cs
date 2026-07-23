namespace TimeLogger.Application.Services;

/// <summary>Posts a message to the configured Slack channel (incoming webhook).</summary>
public interface ISlackMessageSender
{
    /// <summary>Returns false when no webhook is configured or the post failed.</summary>
    Task<bool> SendAsync(string text, CancellationToken ct = default);
}
