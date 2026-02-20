using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.FileImport;

public sealed class FileImportService(
    AppDbContext db,
    IApplyMappingsService mappingService,
    ILogger<FileImportService> logger) : IFileImportService
{
    // Known column name aliases (lower-cased)
    private static readonly string[] DateAliases        = ["date", "workdate", "work date", "work_date", "day"];
    private static readonly string[] HoursAliases       = ["hours", "duration", "time", "timespent", "time spent", "time_spent", "h"];
    private static readonly string[] EmailAliases       = ["email", "useremail", "user email", "user_email", "user", "author"];
    private static readonly string[] DescAliases        = ["description", "comment", "notes", "note", "desc", "summary"];
    private static readonly string[] ProjectAliases     = ["projectkey", "project key", "project_key", "project", "proj"];
    private static readonly string[] IssueAliases       = ["issuekey", "issue key", "issue_key", "issue", "ticket", "jira"];
    private static readonly string[] ActivityAliases    = ["activity", "type", "category", "work type"];

    private static readonly HashSet<string> KnownAliases =
        [.. DateAliases, .. HoursAliases, .. EmailAliases, .. DescAliases,
           .. ProjectAliases, .. IssueAliases, .. ActivityAliases];

    static FileImportService() =>
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

    public async Task<FileImportResult> ImportAsync(
        int sourceId, Stream stream, string fileName, CancellationToken ct = default)
    {
        logger.LogInformation("Starting file import: {FileName} for source {SourceId}", fileName, sourceId);

        List<ParsedRow> rows;
        var errors = new List<string>();

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            rows = ext is ".xlsx" or ".xls" or ".xlsm"
                ? ParseExcel(stream, errors)
                : ParseCsv(stream, errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse file {FileName}", fileName);
            return new FileImportResult(0, 0, 0, [$"File parse failed: {ex.Message}"]);
        }

        if (rows.Count == 0 && errors.Count > 0)
            return new FileImportResult(0, 0, 0, errors);

        // Load existing ExternalIds to skip duplicates
        var existingIds = await db.ImportedEntries
            .Where(e => e.ImportSourceId == sourceId)
            .Select(e => e.ExternalId)
            .ToHashSetAsync(ct);

        int imported = 0, skipped = 0;
        var newEntries = new List<ImportedEntry>();

        foreach (var row in rows)
        {
            var externalId = ComputeExternalId(sourceId, row);
            if (existingIds.Contains(externalId))
            {
                skipped++;
                continue;
            }

            newEntries.Add(new ImportedEntry
            {
                ImportSourceId      = sourceId,
                ExternalId          = externalId,
                WorkDate            = row.WorkDate,
                TimeSpentSeconds    = row.TimeSpentSeconds,
                UserEmail           = row.UserEmail,
                Description         = row.Description,
                ProjectKey          = row.ProjectKey,
                IssueKey            = row.IssueKey,
                Activity            = row.Activity,
                MetadataJson        = row.ExtraColumns.Count > 0
                    ? JsonSerializer.Serialize(row.ExtraColumns)
                    : null,
                Status              = ImportStatus.Pending,
                ImportedAt          = DateTimeOffset.UtcNow,
            });
            existingIds.Add(externalId);
            imported++;
        }

        if (newEntries.Count > 0)
        {
            db.ImportedEntries.AddRange(newEntries);

            // Update LastPolledAt on the source
            var source = await db.ImportSources.FindAsync([sourceId], ct);
            if (source is not null)
                source.LastPolledAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            // Auto-apply mapping rules to the newly imported entries
            await mappingService.ApplyAllPendingAsync(ct);
        }

        logger.LogInformation(
            "File import complete: {Total} rows, {Imported} imported, {Skipped} skipped, {Errors} errors",
            rows.Count, imported, skipped, errors.Count);

        return new FileImportResult(rows.Count, imported, skipped, errors);
    }

    // ------------------------------------------------------------------
    // CSV parsing
    // ------------------------------------------------------------------

    private static List<ParsedRow> ParseCsv(Stream stream, List<string> errors)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        var rows = new List<ParsedRow>();
        int rowNum = 2;

        while (csv.Read())
        {
            var rawRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                rawRow[h] = csv.GetField(h)?.Trim() ?? "";

            if (TryParseRow(rawRow, rowNum, errors, out var parsed))
                rows.Add(parsed!);

            rowNum++;
        }

        return rows;
    }

    // ------------------------------------------------------------------
    // Excel parsing
    // ------------------------------------------------------------------

    private static List<ParsedRow> ParseExcel(Stream stream, List<string> errors)
    {
        // EPPlus requires seekable stream
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var package = new ExcelPackage(ms);
        var sheet = package.Workbook.Worksheets.Count > 0
            ? package.Workbook.Worksheets[0]
            : throw new InvalidOperationException("Excel file contains no worksheets.");

        if (sheet.Dimension is null)
            return [];

        int totalCols = sheet.Dimension.Columns;

        // Build header map: col index → header name
        var headers = new Dictionary<int, string>();
        for (int col = 1; col <= totalCols; col++)
        {
            var h = sheet.Cells[1, col].GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(h))
                headers[col] = h;
        }

        var rows = new List<ParsedRow>();

        for (int row = 2; row <= sheet.Dimension.Rows; row++)
        {
            var rawRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, header) in headers)
            {
                var val = sheet.Cells[row, col].GetValue<object>()?.ToString()?.Trim() ?? "";
                rawRow[header] = val;
            }

            // Skip completely empty rows
            if (rawRow.Values.All(string.IsNullOrWhiteSpace))
                continue;

            if (TryParseRow(rawRow, row, errors, out var parsed))
                rows.Add(parsed!);
        }

        return rows;
    }

    // ------------------------------------------------------------------
    // Row parsing helpers
    // ------------------------------------------------------------------

    private sealed record ParsedRow(
        DateOnly WorkDate,
        int TimeSpentSeconds,
        string UserEmail,
        string? Description,
        string? ProjectKey,
        string? IssueKey,
        string? Activity,
        Dictionary<string, string> ExtraColumns);

    private static bool TryParseRow(
        Dictionary<string, string> raw, int rowNum, List<string> errors,
        out ParsedRow? parsed)
    {
        parsed = null;
        var rowErrors = new List<string>();

        // --- Required fields ---
        var dateStr  = FindField(raw, DateAliases);
        var hoursStr = FindField(raw, HoursAliases);
        var email    = FindField(raw, EmailAliases);

        if (dateStr is null)  rowErrors.Add($"Row {rowNum}: missing date column.");
        if (hoursStr is null) rowErrors.Add($"Row {rowNum}: missing hours column.");
        if (email is null)    rowErrors.Add($"Row {rowNum}: missing email column.");

        if (rowErrors.Count > 0) { errors.AddRange(rowErrors); return false; }

        if (!TryParseDate(dateStr!, out var workDate))
        {
            errors.Add($"Row {rowNum}: cannot parse date '{dateStr}'.");
            return false;
        }

        if (!TryParseHours(hoursStr!, out var timeSpentSeconds) || timeSpentSeconds <= 0)
        {
            errors.Add($"Row {rowNum}: cannot parse hours '{hoursStr}'.");
            return false;
        }

        // --- Optional fields ---
        var description = FindField(raw, DescAliases);
        var projectKey  = FindField(raw, ProjectAliases);
        var issueKey    = FindField(raw, IssueAliases);
        var activity    = FindField(raw, ActivityAliases);

        // --- Extra columns → metadata ---
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !KnownAliases.Contains(key.ToLowerInvariant()))
            {
                extra[key] = value;
            }
        }

        parsed = new ParsedRow(workDate, timeSpentSeconds, email!, description,
            projectKey, issueKey, activity, extra);
        return true;
    }

    private static string? FindField(Dictionary<string, string> row, string[] aliases)
    {
        foreach (var alias in aliases)
            if (row.TryGetValue(alias, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }

    private static bool TryParseDate(string value, out DateOnly result)
    {
        result = default;
        value = value.Trim();

        string[] formats =
        [
            "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy",
            "d.M.yyyy", "d.M.yy", "yyyy/MM/dd",
            "dd-MM-yyyy", "MM-dd-yyyy",
        ];

        foreach (var fmt in formats)
            if (DateOnly.TryParseExact(value, fmt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out result))
                return true;

        if (DateOnly.TryParse(value, out result))
            return true;

        // Excel stores dates as OA dates (double)
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate))
        {
            try
            {
                result = DateOnly.FromDateTime(DateTime.FromOADate(oaDate));
                return true;
            }
            catch { /* not a valid OA date */ }
        }

        return false;
    }

    private static bool TryParseHours(string value, out int seconds)
    {
        seconds = 0;
        value = value.Trim();

        // "1:30" or "1:30:00"
        if (TimeSpan.TryParseExact(value, [@"h\:mm", @"hh\:mm", @"h\:mm\:ss"], null, out var ts))
        {
            seconds = (int)ts.TotalSeconds;
            return true;
        }

        // Decimal hours "1.5"
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
        {
            seconds = (int)(hours * 3600);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Deduplication
    // ------------------------------------------------------------------

    private static string ComputeExternalId(int sourceId, ParsedRow row)
    {
        var key = $"{sourceId}|{row.WorkDate:yyyy-MM-dd}|{row.UserEmail}|{row.TimeSpentSeconds}|{row.Description}|{row.ProjectKey}|{row.IssueKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "file-" + Convert.ToHexString(hash)[..24];
    }
}
