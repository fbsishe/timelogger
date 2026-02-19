using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Jira;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Tempo;

public class TempoImportService(
    IHttpClientFactory httpClientFactory,
    IJiraApiClient jiraClient,
    AppDbContext db,
    IOptions<TempoOptions> tempoOptions,
    ILogger<TempoImportService> logger) : ITempoImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<int> ImportAsync(
        int importSourceId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var source = await db.ImportSources.FindAsync([importSourceId], cancellationToken);
        if (source is null)
            throw new InvalidOperationException($"ImportSource {importSourceId} not found.");

        if (string.IsNullOrWhiteSpace(source.ApiToken))
            throw new InvalidOperationException($"ImportSource {importSourceId} has no API token configured.");

        logger.LogInformation(
            "Importing Tempo worklogs for source '{Source}' from {From} to {To}",
            source.Name, from, to);

        var worklogs = await FetchAllWorklogsAsync(source.ApiToken, from, to, cancellationToken);
        logger.LogInformation("Fetched {Count} worklogs from Tempo", worklogs.Count);

        // Load existing external IDs to deduplicate
        var existingIds = await db.ImportedEntries
            .Where(e => e.ImportSourceId == importSourceId)
            .Select(e => e.ExternalId)
            .ToHashSetAsync(cancellationToken);

        int imported = 0;

        foreach (var worklog in worklogs)
        {
            var externalId = worklog.TempoWorklogId.ToString();
            if (existingIds.Contains(externalId))
                continue;

            // Enrich with Jira issue details (project key, custom fields)
            string? projectKey = null;
            string? issueKey = null;
            Dictionary<string, JsonElement>? customFields = null;

            if (worklog.Issue?.Id is > 0)
            {
                try
                {
                    var issue = await jiraClient.GetIssueAsync(worklog.Issue.Id, cancellationToken: cancellationToken);
                    projectKey = issue.Fields?.Project?.Key;
                    issueKey = issue.Key;
                    customFields = issue.Fields?.ExtensionData;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch Jira issue {IssueId} for worklog {WorklogId}",
                        worklog.Issue.Id, worklog.TempoWorklogId);
                }
            }

            var metadataJson = BuildMetadataJson(worklog, customFields);

            var entry = new ImportedEntry
            {
                ImportSourceId = importSourceId,
                ExternalId = externalId,
                UserEmail = worklog.Author?.AccountId ?? "unknown",
                WorkDate = DateOnly.Parse(worklog.StartDate),
                TimeSpentSeconds = worklog.TimeSpentSeconds,
                Description = worklog.Description,
                ProjectKey = projectKey,
                IssueKey = issueKey,
                MetadataJson = metadataJson,
                Status = ImportStatus.Pending,
                ImportedAt = DateTimeOffset.UtcNow,
            };

            db.ImportedEntries.Add(entry);
            imported++;
        }

        if (imported > 0)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Imported {NewCount} new entries (skipped {Skipped} duplicates)",
            imported, worklogs.Count - imported);

        return imported;
    }

    public async Task ImportYesterdayAsync(CancellationToken cancellationToken = default)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var sources = await db.ImportSources
            .Where(s => s.SourceType == SourceType.Tempo && s.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var source in sources)
        {
            try
            {
                await ImportAsync(source.Id, yesterday, yesterday, cancellationToken);
                source.LastPolledAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to import worklogs for source '{Source}'", source.Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<List<Dto.TempoWorklogDto>> FetchAllWorklogsAsync(
        string apiToken,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // Build a per-call HttpClient with the source-specific token
        var client = httpClientFactory.CreateClient("Tempo");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiToken);

        const int limit = 5000;
        var all = new List<Dto.TempoWorklogDto>();
        int offset = 0;

        while (true)
        {
            var url = $"{tempoOptions.Value.BaseUrl}/worklogs" +
                      $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" +
                      $"&offset={offset}&limit={limit}";

            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<Dto.TempoPagedResponse<Dto.TempoWorklogDto>>(json, JsonOptions)
                       ?? new Dto.TempoPagedResponse<Dto.TempoWorklogDto>();

            all.AddRange(page.Results);

            // Stop if we got everything (no next page)
            if (string.IsNullOrEmpty(page.Metadata?.Next) || page.Results.Count < limit)
                break;

            offset += limit;
        }

        return all;
    }

    private static string BuildMetadataJson(
        Dto.TempoWorklogDto worklog,
        Dictionary<string, JsonElement>? customFields)
    {
        var meta = new Dictionary<string, object?>
        {
            ["billableSeconds"] = worklog.BillableSeconds,
            ["startTime"] = worklog.StartTime,
        };

        // Tempo work attributes (e.g. _WorkType_)
        if (worklog.Attributes?.Values is { Count: > 0 } attrs)
        {
            foreach (var attr in attrs)
                meta[$"attr_{attr.Key}"] = attr.Value;
        }

        // Jira custom fields (e.g. customfield_10200)
        if (customFields is not null)
        {
            foreach (var (key, value) in customFields)
            {
                if (key.StartsWith("customfield_", StringComparison.Ordinal))
                    meta[key] = value.ValueKind == JsonValueKind.Null ? null : (object?)value.ToString();
            }
        }

        return JsonSerializer.Serialize(meta);
    }
}
