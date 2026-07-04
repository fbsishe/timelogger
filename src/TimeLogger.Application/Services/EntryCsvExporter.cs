using System.Text;

namespace TimeLogger.Application.Services;

/// <summary>Renders entry list items as RFC 4180 CSV for the export feature.</summary>
public static class EntryCsvExporter
{
    public static string ToCsv(IEnumerable<EntryListItem> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Person,Source,IssueKey,ProjectKey,Description,Hours,Status");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(',',
                e.WorkDate.ToString("yyyy-MM-dd"),
                Escape(e.UserEmail),
                Escape(e.SourceName),
                Escape(e.IssueKey),
                Escape(e.ProjectKey),
                Escape(e.Description),
                e.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Escape(e.Status)));
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
