namespace TimeLogger.Application.Services;

/// <summary>Result of a file import operation.</summary>
public record FileImportResult(
    int TotalRows,
    int Imported,
    int Skipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// Parses a CSV or Excel file (auto-detected by extension) and persists
/// its rows as <see cref="TimeLogger.Domain.Entities.ImportedEntry"/> records.
/// Rows already imported are silently skipped (content-hash deduplication).
/// After saving, auto-applies all enabled mapping rules to the new entries.
/// </summary>
public interface IFileImportService
{
    /// <param name="sourceId">ID of the FileUpload <see cref="TimeLogger.Domain.Entities.ImportSource"/>.</param>
    /// <param name="stream">File content stream.</param>
    /// <param name="fileName">Original file name (used for format detection and dedup hashing).</param>
    Task<FileImportResult> ImportAsync(
        int sourceId,
        Stream stream,
        string fileName,
        CancellationToken ct = default);
}
