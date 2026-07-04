using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Refit;
using System.Net;
using TimeLogger.Application.Interfaces;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Timelog;
using TimeLogger.Infrastructure.Timelog.Dto;

namespace TimeLogger.Infrastructure.Tests.Timelog;

public class TimelogSubmissionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITimelogApiClient> _apiClientMock;
    private readonly TimelogSubmissionService _sut;

    public TimelogSubmissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _apiClientMock = new Mock<ITimelogApiClient>();
        _sut = CreateSut();
    }

    private TimelogSubmissionService CreateSut(int retryCount = 1) =>
        new(
            _apiClientMock.Object,
            _db,
            Options.Create(new TimelogOptions
            {
                BaseUrl = "https://timelog.test/api",
                ApiKey = "test-key",
                SubmitRetryCount = retryCount,
                SubmitRetryBaseDelaySeconds = 0,
            }),
            NullLogger<TimelogSubmissionService>.Instance);

    private async Task<(ImportedEntry entry, TimelogTask task)> SeedEntryWithTaskAsync()
    {
        var project = new TimelogProject { ExternalId = "proj-1", Name = "Proj", LastSyncedAt = DateTimeOffset.UtcNow };
        _db.TimelogProjects.Add(project);
        await _db.SaveChangesAsync();

        var task = new TimelogTask { ExternalId = "guid-999", ApiTaskId = 999, Name = "Dev Task", TimelogProjectId = project.Id, LastSyncedAt = DateTimeOffset.UtcNow };
        _db.TimelogTasks.Add(task);
        await _db.SaveChangesAsync();

        var source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();

        var entry = new ImportedEntry
        {
            ExternalId = "w-123",
            UserEmail = "dev@example.com",
            WorkDate = new DateOnly(2024, 1, 15),
            TimeSpentSeconds = 7200, // 2h
            Description = "Feature work",
            Status = ImportStatus.Mapped,
            ImportSourceId = source.Id,
            TimelogTaskId = task.Id,
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();

        return (entry, task);
    }

    [Fact]
    public async Task SubmitAsync_OnSuccess_MarksEntrySubmittedAndPersistsAudit()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();

        var successResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.OK), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResponse);

        await _sut.SubmitAsync(entry);

        Assert.Equal(ImportStatus.Submitted, entry.Status);

        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(SubmissionStatus.Success, audit.Status);
        Assert.Equal(entry.Id, audit.ImportedEntryId);
        Assert.Equal(1, audit.AttemptCount);
        Assert.Null(audit.ErrorMessage);
        Assert.NotNull(audit.ExternalId); // idempotency GUID stored
    }

    [Fact]
    public async Task SubmitAsync_OnApiFailure_MarksEntryFailedAndPersistsError()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();

        var failResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failResponse);

        await _sut.SubmitAsync(entry);

        Assert.Equal(ImportStatus.Failed, entry.Status);

        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(SubmissionStatus.Failed, audit.Status);
        Assert.NotNull(audit.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_OnException_MarksEntryFailed()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        await _sut.SubmitAsync(entry);

        Assert.Equal(ImportStatus.Failed, entry.Status);
        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(SubmissionStatus.Failed, audit.Status);
        Assert.Contains("Network error", audit.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_IncreasesAttemptCountOnRetry()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();

        // First attempt — fail
        var failResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.InternalServerError), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failResponse);

        await _sut.SubmitAsync(entry);

        // Second attempt — success
        var successResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.OK), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResponse);

        // Re-fetch to get updated entry
        var refreshedEntry = await _db.ImportedEntries.FindAsync(entry.Id);
        await _sut.SubmitAsync(refreshedEntry!);

        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(SubmissionStatus.Success, audit.Status);
        Assert.Equal(2, audit.AttemptCount);
    }

    [Fact]
    public async Task SubmitAsync_SkipsEntryWithNoMappedTask()
    {
        var source = new ImportSource { Name = "Tempo", SourceType = SourceType.Tempo };
        _db.ImportSources.Add(source);
        await _db.SaveChangesAsync();

        var entry = new ImportedEntry
        {
            ExternalId = "w-no-task",
            UserEmail = "dev@example.com",
            WorkDate = new DateOnly(2024, 1, 15),
            TimeSpentSeconds = 3600,
            Status = ImportStatus.Pending,
            ImportSourceId = source.Id,
            TimelogTaskId = null, // no mapping
        };
        _db.ImportedEntries.Add(entry);
        await _db.SaveChangesAsync();

        await _sut.SubmitAsync(entry);

        _apiClientMock.Verify(
            c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.Equal(0, await _db.SubmittedEntries.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_SendsCorrectHoursFromTimeSpentSeconds()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();
        // 7200 seconds = 2.0 hours

        CreateTimeRegistrationDto? capturedDto = null;

        var successResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.OK), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTimeRegistrationDto, CancellationToken>((dto, _) => capturedDto = dto)
            .ReturnsAsync(successResponse);

        await _sut.SubmitAsync(entry);

        Assert.NotNull(capturedDto);
        Assert.Equal(2.0, capturedDto.Hours);
        Assert.Equal("999", capturedDto.TaskId.ToString());
        Assert.Equal("2024-01-15", capturedDto.Date);
    }

    // ------------------------------------------------------------------
    // Retry with exponential backoff (TL-32)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_TransientException_RetriesAndSucceeds()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();
        var sut = CreateSut(retryCount: 3);

        var successResponse = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.OK), null, new RefitSettings());

        _apiClientMock
            .SetupSequence(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection reset"))
            .ThrowsAsync(new HttpRequestException("Connection reset"))
            .ReturnsAsync(successResponse);

        var outcome = await sut.SubmitAsync(entry);

        Assert.Equal(SubmitOutcome.Succeeded, outcome);
        Assert.Equal(ImportStatus.Submitted, entry.Status);
        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(SubmissionStatus.Success, audit.Status);
        Assert.Equal(3, audit.AttemptCount);
        _apiClientMock.Verify(
            c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task SubmitAsync_ServerError_RetriesUpToLimit_ThenFails()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();
        var sut = CreateSut(retryCount: 3);

        var serverError = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(serverError);

        var outcome = await sut.SubmitAsync(entry);

        Assert.Equal(SubmitOutcome.Failed, outcome);
        Assert.Equal(ImportStatus.Failed, entry.Status);
        var audit = await _db.SubmittedEntries.SingleAsync();
        Assert.Equal(3, audit.AttemptCount);
        _apiClientMock.Verify(
            c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task SubmitAsync_ClientError_DoesNotRetry()
    {
        var (entry, _) = await SeedEntryWithTaskAsync();
        var sut = CreateSut(retryCount: 3);

        var validationError = new ApiResponse<object>(
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity), null, new RefitSettings());

        _apiClientMock
            .Setup(c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationError);

        var outcome = await sut.SubmitAsync(entry);

        Assert.Equal(SubmitOutcome.Failed, outcome);
        _apiClientMock.Verify(
            c => c.CreateTimeRegistrationAsync(It.IsAny<CreateTimeRegistrationDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose() => _db.Dispose();
}
