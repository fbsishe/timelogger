using TimeLogger.Application.Services;

namespace TimeLogger.Application.Tests.Services;

public class EntryCsvExporterTests
{
    private static EntryListItem MakeItem(
        string? description = "Regular work",
        string? userEmail = "Jane Doe",
        double hours = 2.5) =>
        new(
            Id: 1,
            ExternalId: "w-1",
            SourceName: "Tempo",
            WorkDate: new DateOnly(2026, 7, 1),
            Hours: hours,
            ProjectKey: "PROJ",
            IssueKey: "PROJ-42",
            Description: description,
            UserEmail: userEmail,
            Status: "Mapped",
            MetadataJson: null);

    [Fact]
    public void ToCsv_ContainsHeaderAndRow()
    {
        var csv = EntryCsvExporter.ToCsv([MakeItem()]);

        var lines = csv.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("Date,Person,Source,IssueKey,ProjectKey,Description,Hours,Status", lines[0]);
        Assert.Equal("2026-07-01,Jane Doe,Tempo,PROJ-42,PROJ,Regular work,2.5,Mapped", lines[1]);
    }

    [Fact]
    public void ToCsv_QuotesFieldsWithCommasAndQuotes()
    {
        var csv = EntryCsvExporter.ToCsv([MakeItem(description: "Fixed \"urgent\" bug, deployed")]);

        Assert.Contains("\"Fixed \"\"urgent\"\" bug, deployed\"", csv);
    }

    [Fact]
    public void ToCsv_HandlesNullFields()
    {
        var item = MakeItem(description: null, userEmail: null);

        var csv = EntryCsvExporter.ToCsv([item]);

        var lines = csv.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("2026-07-01,,Tempo,PROJ-42,PROJ,,2.5,Mapped", lines[1]);
    }

    [Fact]
    public void ToCsv_UsesInvariantDecimalSeparator()
    {
        var csv = EntryCsvExporter.ToCsv([MakeItem(hours: 1.25)]);

        Assert.Contains(",1.25,", csv);
    }
}
