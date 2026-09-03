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

    /// <summary>Statuses whose entries we may still rewrite — nothing has gone to Timelog yet.</summary>
    private static readonly ImportStatus[] RefreshableStatuses =
        [ImportStatus.Pending, ImportStatus.Mapped, ImportStatus.Failed, ImportStatus.Ignored];

    public async Task<int> ImportAsync(
        int importSourceId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var source = await db.ImportSources.FindAsync([importSourceId], cancellationToken);
        if (source is null)
            throw new InvalidOperationException($"ImportSource {importSourceId} not found.");

        var result = await ImportCoreAsync(source, from, to, updatedFrom: null, cancellationToken);
        return result.Imported;
    }

    public async Task<TempoImportResult> ImportIncrementalAsync(CancellationToken cancellationToken = default)
    {
        var sources = await db.ImportSources
            .Where(s => s.SourceType == SourceType.Tempo && s.IsEnabled)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-Math.Abs(tempoOptions.Value.LookbackDays));
        var total = new TempoImportResult(0, 0, 0);

        foreach (var source in sources)
        {
            // Capture the fetch start *before* the call: anything amended while we are
            // running must be caught by the next pull, not skipped.
            var pollStartedAt = DateTimeOffset.UtcNow;

            // A null watermark means this source has never polled successfully — fall back to
            // a plain window sweep so the first run backfills instead of pulling all of history.
            var watermark = source.LastPolledAt?
                .AddMinutes(-Math.Abs(tempoOptions.Value.WatermarkOverlapMinutes));

            try
            {
                var result = await ImportCoreAsync(source, from, today, watermark, cancellationToken);
                total = new TempoImportResult(
                    total.Imported + result.Imported,
                    total.Refreshed + result.Refreshed,
                    total.ChangedAfterSubmission + result.ChangedAfterSubmission);

                source.LastPolledAt = pollStartedAt;
            }
            catch (Exception ex)
            {
                // Leave LastPolledAt untouched so the next run retries this same span.
                logger.LogError(ex, "Failed to import worklogs for source '{Source}'", source.Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Incremental Tempo pull: {Imported} new, {Refreshed} refreshed, {Stuck} changed after submission",
            total.Imported, total.Refreshed, total.ChangedAfterSubmission);

        return total;
    }

    // ------------------------------------------------------------------
    // Core
    // ------------------------------------------------------------------

    private async Task<TempoImportResult> ImportCoreAsync(
        ImportSource source,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? updatedFrom,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ApiToken))
            throw new InvalidOperationException($"ImportSource {source.Id} has no API token configured.");

        logger.LogInformation(
            "Importing Tempo worklogs for source '{Source}' from {From} to {To}{Incremental}",
            source.Name, from, to,
            updatedFrom is null ? "" : $" (updated since {updatedFrom:u})");

        var worklogs = await FetchAllWorklogsAsync(source.ApiToken, from, to, updatedFrom, cancellationToken);
        logger.LogInformation("Fetched {Count} worklogs from Tempo", worklogs.Count);

        if (worklogs.Count == 0)
            return new TempoImportResult(0, 0, 0);

        var existing = await LoadExistingAsync(source.Id, worklogs, cancellationToken);

        int imported = 0, refreshed = 0, changedAfterSubmission = 0, unchanged = 0, touched = 0, newlyAmended = 0;

        foreach (var worklog in worklogs)
        {
            var externalId = worklog.TempoWorklogId.ToString();

            if (existing.TryGetValue(externalId, out var entry))
            {
                switch (Reconcile(entry, worklog))
                {
                    case ReconcileOutcome.Unchanged:
                        unchanged++;
                        continue;
                    case ReconcileOutcome.TimestampOnly:
                        // Record it so later pulls stop re-examining this worklog.
                        entry.SourceUpdatedAt = worklog.UpdatedAt;
                        unchanged++;
                        touched++;
                        continue;
                    case ReconcileOutcome.BlockedBySubmission:
                        changedAfterSubmission++;
                        // Flag it rather than rewrite it, and re-announce only when the
                        // amendment itself is new (a second edit deserves a second mention).
                        if (entry.AmendedAfterSubmissionAt != worklog.UpdatedAt)
                        {
                            entry.AmendedAfterSubmissionAt = worklog.UpdatedAt ?? DateTimeOffset.UtcNow;
                            entry.AmendmentReportedAt = null;
                            newlyAmended++;
                        }
                        entry.AmendedSourceSeconds = worklog.TimeSpentSeconds;
                        entry.AmendedSourceDescription = worklog.Description;
                        touched++;

                        logger.LogWarning(
                            "Tempo worklog {WorklogId} was amended after we submitted entry {EntryId} to Timelog "
                            + "({OldHours:F2}h -> {NewHours:F2}h) — left untouched, flagged for review",
                            worklog.TempoWorklogId, entry.Id,
                            entry.TimeSpentSeconds / 3600.0, worklog.TimeSpentSeconds / 3600.0);
                        continue;
                    case ReconcileOutcome.Refresh:
                        var enrichment = await EnrichAsync(worklog, cancellationToken);
                        Apply(entry, worklog, enrichment);
                        // Re-run the mapping engine over the amended values.
                        entry.Status = ImportStatus.Pending;
                        entry.MappingRuleId = null;
                        ClearAmendmentFlag(entry);
                        refreshed++;
                        continue;
                }
            }

            var newEnrichment = await EnrichAsync(worklog, cancellationToken);
            var created = new ImportedEntry
            {
                ImportSourceId = source.Id,
                ExternalId = externalId,
                UserEmail = worklog.Author?.AccountId ?? "unknown",
                WorkDate = DateOnly.Parse(worklog.StartDate),
                Status = ImportStatus.Pending,
                ImportedAt = DateTimeOffset.UtcNow,
            };
            Apply(created, worklog, newEnrichment);

            db.ImportedEntries.Add(created);
            imported++;
        }

        if (imported > 0 || refreshed > 0 || touched > 0)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Imported {NewCount} new, refreshed {Refreshed}, skipped {Unchanged} unchanged, "
            + "{Blocked} amended after submission ({NewlyAmended} newly flagged)",
            imported, refreshed, unchanged, changedAfterSubmission, newlyAmended);

        return new TempoImportResult(imported, refreshed, changedAfterSubmission);
    }

    private enum ReconcileOutcome { Unchanged, TimestampOnly, Refresh, BlockedBySubmission }

    /// <summary>
    /// Decides what to do with a worklog we have already imported. Tempo's <c>updatedAt</c> is
    /// authoritative when present; otherwise fall back to comparing the fields we store.
    /// </summary>
    private static ReconcileOutcome Reconcile(ImportedEntry entry, Dto.TempoWorklogDto worklog)
    {
        if (worklog.UpdatedAt is { } updatedAt
            && entry.SourceUpdatedAt is { } seen
            && updatedAt <= seen)
        {
            return ReconcileOutcome.Unchanged;
        }

        // The timestamp moved (or we have never recorded one). Only re-map if something we
        // actually use moved with it — a bump on its own just gets recorded.
        if (!HasMaterialDifference(entry, worklog))
            return ReconcileOutcome.TimestampOnly;

        return RefreshableStatuses.Contains(entry.Status)
            ? ReconcileOutcome.Refresh
            : ReconcileOutcome.BlockedBySubmission;
    }

    private static bool HasMaterialDifference(ImportedEntry entry, Dto.TempoWorklogDto worklog) =>
        entry.TimeSpentSeconds != worklog.TimeSpentSeconds
        || entry.Description != worklog.Description
        || entry.WorkDate != DateOnly.Parse(worklog.StartDate)
        || entry.UserEmail != (worklog.Author?.AccountId ?? "unknown");

    private record Enrichment(string? ProjectKey, string? IssueKey, Dictionary<string, JsonElement>? CustomFields);

    private async Task<Enrichment> EnrichAsync(Dto.TempoWorklogDto worklog, CancellationToken cancellationToken)
    {
        if (worklog.Issue?.Id is not > 0)
            return new Enrichment(null, null, null);

        try
        {
            var issue = await jiraClient.GetIssueAsync(worklog.Issue.Id, cancellationToken: cancellationToken);
            return new Enrichment(issue.Fields?.Project?.Key, issue.Key, issue.Fields?.ExtensionData);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Jira issue {IssueId} for worklog {WorklogId}",
                worklog.Issue.Id, worklog.TempoWorklogId);
            return new Enrichment(null, null, null);
        }
    }

    /// <summary>The entry now agrees with the source again, so any pending discrepancy is moot.</summary>
    private static void ClearAmendmentFlag(ImportedEntry entry)
    {
        entry.AmendedAfterSubmissionAt = null;
        entry.AmendedSourceSeconds = null;
        entry.AmendedSourceDescription = null;
        entry.AmendmentReportedAt = null;
    }

    private static void Apply(ImportedEntry entry, Dto.TempoWorklogDto worklog, Enrichment enrichment)
    {
        entry.UserEmail = worklog.Author?.AccountId ?? "unknown";
        entry.WorkDate = DateOnly.Parse(worklog.StartDate);
        entry.TimeSpentSeconds = worklog.TimeSpentSeconds;
        entry.Description = worklog.Description;
        entry.ProjectKey = enrichment.ProjectKey;
        entry.IssueKey = enrichment.IssueKey;
        entry.MetadataJson = BuildMetadataJson(worklog, enrichment.CustomFields);
        entry.SourceUpdatedAt = worklog.UpdatedAt;
    }

    /// <summary>
    /// Loads the entries matching the fetched worklogs, in chunks — a wide incremental pull can
    /// return thousands of IDs, well past SQL Server's parameter ceiling for a single IN clause.
    /// </summary>
    private async Task<Dictionary<string, ImportedEntry>> LoadExistingAsync(
        int importSourceId,
        List<Dto.TempoWorklogDto> worklogs,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 500;
        var ids = worklogs.Select(w => w.TempoWorklogId.ToString()).Distinct().ToList();
        var found = new Dictionary<string, ImportedEntry>(ids.Count);

        foreach (var chunk in ids.Chunk(chunkSize))
        {
            var rows = await db.ImportedEntries
                .Where(e => e.ImportSourceId == importSourceId && chunk.Contains(e.ExternalId))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
                found[row.ExternalId] = row;
        }

        return found;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<List<Dto.TempoWorklogDto>> FetchAllWorklogsAsync(
        string apiToken,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? updatedFrom,
        CancellationToken cancellationToken)
    {
        // Build a per-call HttpClient with the source-specific token
        var client = httpClientFactory.CreateClient("Tempo");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiToken);

        const int limit = 5000;
        var all = new List<Dto.TempoWorklogDto>();
        int offset = 0;

        // Tempo's docs claim updatedFrom cannot be combined with other parameters; verified
        // against the tenant on 2026-09-03 that from/to *are* still applied alongside it.
        var updatedFromParam = updatedFrom is null
            ? ""
            : $"&updatedFrom={updatedFrom.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss}Z";

        while (true)
        {
            var url = $"{tempoOptions.Value.BaseUrl}/worklogs" +
                      $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" +
                      updatedFromParam +
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

        if (worklog.CreatedAt is { } createdAt)
            meta["createdAt"] = createdAt.UtcDateTime.ToString("u");

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
