using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TimeLogger.Domain;
using TimeLogger.Domain.Entities;
using static Microsoft.Playwright.Assertions;

namespace TimeLogger.E2E;

/// <summary>TL-62 — submission happy path: mapped entry → preview → confirm → Submitted.</summary>
[Collection("E2E")]
public class SubmissionE2ETests(E2EAppFixture fixture)
{
    private const string EntryDescription = "E2E submission entry";

    private async Task<int> SeedMappedEntryAsync()
    {
        await using var db = fixture.CreateDbContext();

        // Re-runs: drop leftovers from previous executions
        var stale = await db.ImportedEntries.Where(e => e.Description == EntryDescription).ToListAsync();
        var staleIds = stale.Select(e => e.Id).ToList();
        db.SubmittedEntries.RemoveRange(db.SubmittedEntries.Where(s => staleIds.Contains(s.ImportedEntryId)));
        db.ImportedEntries.RemoveRange(stale);
        await db.SaveChangesAsync();

        var source = await db.ImportSources.FirstOrDefaultAsync(s => s.Name == "E2E Source");
        if (source is null)
        {
            source = new ImportSource { Name = "E2E Source", SourceType = SourceType.Tempo, IsEnabled = false };
            db.ImportSources.Add(source);
        }

        var project = await db.TimelogProjects.FirstOrDefaultAsync(p => p.Name == "E2E Submission Project");
        if (project is null)
        {
            project = new TimelogProject
            {
                ExternalId = "e2e-sub-proj",
                Name = "E2E Submission Project",
                IsActive = true,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.TimelogProjects.Add(project);
        }

        var task = await db.TimelogTasks.FirstOrDefaultAsync(t => t.ExternalId == "e2e-sub-task");
        if (task is null)
        {
            task = new TimelogTask
            {
                ExternalId = "e2e-sub-task",
                ApiTaskId = 990001,
                Name = "E2E Dev Task",
                IsActive = true,
                TimelogProject = project,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.TimelogTasks.Add(task);
        }

        await db.SaveChangesAsync();

        var entry = new ImportedEntry
        {
            ImportSourceId = source.Id,
            ExternalId = $"e2e-{Guid.NewGuid():N}",
            UserEmail = "e2e-account-id",
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            TimeSpentSeconds = 7200,
            Description = EntryDescription,
            ProjectKey = "E2ESUB",
            Status = ImportStatus.Mapped,
            TimelogProjectId = project.Id,
            TimelogTaskId = task.Id,
            ImportedAt = DateTimeOffset.UtcNow,
        };
        db.ImportedEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    [Fact]
    public async Task SubmitAll_HappyPath_EntryEndsUpSubmitted()
    {
        var entryId = await SeedMappedEntryAsync();

        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync($"{E2EAppFixture.AppUrl}/submission");

        // Entry is listed as ready to submit
        await Expect(page.Locator($"tr:has-text('{EntryDescription}')").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Dry-run preview opens with the per-employee breakdown
        await page.Locator("button:has-text('Submit All')").ClickAsync();
        var dialog = page.Locator(".mud-dialog:has-text('Review before submitting')");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(dialog).ToContainTextAsync("2");

        await dialog.Locator("button:has-text('Confirm & Submit')").ClickAsync();
        await Expect(page.Locator(".mud-snackbar")).ToContainTextAsync(
            "Submission job enqueued", new() { Timeout = 15_000 });

        // Hangfire picks up the job, the stub Timelog API accepts the POST
        var submitted = await fixture.WaitForDbAsync(async db =>
        {
            var entry = await db.ImportedEntries.FindAsync(entryId);
            return entry?.Status == ImportStatus.Submitted;
        }, TimeSpan.FromSeconds(90));
        Assert.True(submitted, "Entry did not reach Submitted status within 90s");

        await using (var db = fixture.CreateDbContext())
        {
            var audit = await db.SubmittedEntries.SingleAsync(s => s.ImportedEntryId == entryId);
            Assert.Equal(SubmissionStatus.Success, audit.Status);
        }

        lock (fixture.StubRequests)
            Assert.Contains(fixture.StubRequests, r => r == "POST /v1/time-registration");

        // After a reload the entry only remains in the history table, marked Success
        await page.ReloadAsync();
        await Expect(page.Locator("text=Ready to Submit").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var historyRow = page.Locator($"tr:has-text('{EntryDescription}')");
        await Expect(historyRow).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Expect(historyRow).ToContainTextAsync("Success");

        await page.CloseAsync();
    }
}
