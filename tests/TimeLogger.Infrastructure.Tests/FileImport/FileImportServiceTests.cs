using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeOpenXml;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Services;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.FileImport;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.Infrastructure.Tests.FileImport;

public class FileImportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IApplyMappingsService> _mappingMock;
    private readonly FileImportService _sut;

    public FileImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _mappingMock = new Mock<IApplyMappingsService>();
        _mappingMock
            .Setup(m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _sut = new FileImportService(_db, _mappingMock.Object, NullLogger<FileImportService>.Instance);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<ImportSource> SeedSourceAsync()
    {
        var source = new ImportSource
        {
            Name = "File Upload Test",
            SourceType = SourceType.FileUpload,
            IsEnabled = true,
        };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }

    private static MemoryStream MakeCsvStream(string csvContent)
    {
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream MakeExcelStream(string[] headers, object[][] rows)
    {
        ExcelPackage.License.SetNonCommercialPersonal("TimeLogger");
        var ms = new MemoryStream();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");

            for (int col = 0; col < headers.Length; col++)
                sheet.Cells[1, col + 1].Value = headers[col];

            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < rows[row].Length; col++)
                    sheet.Cells[row + 2, col + 1].Value = rows[row][col];

            package.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    // ------------------------------------------------------------------
    // CSV: basic import
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_ImportsBasicRow()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.Imported);

        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(new DateOnly(2024, 3, 15), entry.WorkDate);
        Assert.Equal(5400, entry.TimeSpentSeconds);
        Assert.Equal("dev@example.com", entry.UserEmail);
    }

    [Fact]
    public async Task ImportAsync_Csv_DecimalHours_Parsed()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(5400, entry.TimeSpentSeconds);
    }

    [Fact]
    public async Task ImportAsync_Csv_HHMMHours_Parsed()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1:30,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal(5400, entry.TimeSpentSeconds);
    }

    // ------------------------------------------------------------------
    // CSV: column aliases
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_ColumnAliases_WorkDate()
    {
        var source = await SeedSourceAsync();
        var csv = "Work Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ImportAsync_Csv_ColumnAliases_UserEmail()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,User Email\n2024-03-15,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ImportAsync_Csv_ColumnAliases_Duration()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Duration,Email\n2024-03-15,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    // ------------------------------------------------------------------
    // CSV: optional fields
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_OptionalFields_Stored()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email,Description,Project,Issue,Activity\n" +
                  "2024-03-15,1.5,dev@example.com,Feature work,PROJ,PROJ-42,Development";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.Equal("Feature work", entry.Description);
        Assert.Equal("PROJ", entry.ProjectKey);
        Assert.Equal("PROJ-42", entry.IssueKey);
        Assert.Equal("Development", entry.Activity);
    }

    [Fact]
    public async Task ImportAsync_Csv_ExtraColumns_StoredAsMetadata()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email,TeamName\n2024-03-15,1.5,dev@example.com,Alpha";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(1, result.Imported);
        var entry = await _db.ImportedEntries.SingleAsync();
        Assert.NotNull(entry.MetadataJson);
        Assert.Contains("TeamName", entry.MetadataJson);
    }

    // ------------------------------------------------------------------
    // CSV: deduplication
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_Deduplication_SkipsAlreadyImportedRows()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");
        var second = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
    }

    // ------------------------------------------------------------------
    // CSV: missing required columns
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_MissingDateColumn_ReturnsError()
    {
        var source = await SeedSourceAsync();
        var csv = "Hours,Email\n1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.True(result.Errors.Count > 0);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task ImportAsync_Csv_MissingHoursColumn_ReturnsError()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Email\n2024-03-15,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.True(result.Errors.Count > 0);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task ImportAsync_Csv_MissingEmailColumn_ReturnsError()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours\n2024-03-15,1.5";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.True(result.Errors.Count > 0);
        Assert.Equal(0, result.Imported);
    }

    // ------------------------------------------------------------------
    // CSV: invalid values
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_InvalidDate_ReturnsRowError()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\nnot-a-date,1.5,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.True(result.Errors.Count > 0);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task ImportAsync_Csv_ZeroHours_ReturnsRowError()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,0,dev@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.True(result.Errors.Count > 0);
        Assert.Equal(0, result.Imported);
    }

    // ------------------------------------------------------------------
    // CSV: multiple rows
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Csv_MultipleRows_AllImported()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n" +
                  "2024-03-15,1.0,alice@example.com\n" +
                  "2024-03-16,2.0,bob@example.com\n" +
                  "2024-03-17,3.0,carol@example.com";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(3, result.Imported);
    }

    [Fact]
    public async Task ImportAsync_Csv_EmptyFile_ReturnsZeroRows()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email";

        var result = await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.Imported);
    }

    // ------------------------------------------------------------------
    // Excel
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_Excel_ImportsBasicRow()
    {
        var source = await SeedSourceAsync();
        var stream = MakeExcelStream(
            headers: ["Date", "Hours", "Email"],
            rows: [["2024-03-15", "1.5", "dev@example.com"]]);

        var result = await _sut.ImportAsync(source.Id, stream, "test.xlsx");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ImportAsync_Excel_SkipsEmptyRows()
    {
        var source = await SeedSourceAsync();
        var stream = MakeExcelStream(
            headers: ["Date", "Hours", "Email"],
            rows:
            [
                ["2024-03-15", "1.0", "alice@example.com"],
                ["2024-03-16", "2.0", "bob@example.com"],
                ["", "", ""],
            ]);

        var result = await _sut.ImportAsync(source.Id, stream, "test.xlsx");

        Assert.Equal(2, result.Imported);
    }

    // ------------------------------------------------------------------
    // Post-import side-effects
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_CallsApplyMappingsAfterImport()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        _mappingMock.Verify(
            m => m.ApplyAllPendingAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportAsync_UpdatesSourceLastPolledAt()
    {
        var source = await SeedSourceAsync();
        var csv = "Date,Hours,Email\n2024-03-15,1.5,dev@example.com";

        await _sut.ImportAsync(source.Id, MakeCsvStream(csv), "test.csv");

        var updated = await _db.ImportSources.FindAsync(source.Id);
        Assert.NotNull(updated!.LastPolledAt);
    }

    public void Dispose() => _db.Dispose();
}
