using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TimeLogger.Domain.Entities;
using static Microsoft.Playwright.Assertions;

namespace TimeLogger.E2E;

/// <summary>TL-63 — mapping rule CRUD: create, test-preview, toggle, edit, delete.</summary>
[Collection("E2E")]
public class MappingRulesE2ETests(E2EAppFixture fixture)
{
    private const string RuleName = "E2E Smoke Rule";
    private const string RenamedRule = "E2E Smoke Rule (edited)";

    private async Task SeedProjectAndCleanRulesAsync()
    {
        await using var db = fixture.CreateDbContext();

        var stale = await db.MappingRules
            .Where(r => r.Name.StartsWith("E2E Smoke Rule"))
            .ToListAsync();
        db.MappingRules.RemoveRange(stale);
        await db.SaveChangesAsync();

        var project = await db.TimelogProjects.FirstOrDefaultAsync(p => p.Name == "E2E Rules Project");
        if (project is null)
        {
            project = new TimelogProject
            {
                ExternalId = "e2e-rules-proj",
                Name = "E2E Rules Project",
                IsActive = true,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.TimelogProjects.Add(project);

            db.TimelogTasks.Add(new TimelogTask
            {
                ExternalId = "e2e-rules-task",
                ApiTaskId = 990002,
                Name = "E2E Rules Task",
                IsActive = true,
                TimelogProject = project,
                LastSyncedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Locates a MudBlazor input inside the open dialog by its label text.</summary>
    private static ILocator DialogField(IPage page, string label) =>
        page.Locator($".mud-dialog .mud-input-control:has(label:text-is('{label}'))")
            .Locator("input, textarea")
            .First;

    private static async Task SelectMudOptionAsync(IPage page, string selectLabel, string optionText)
    {
        // MudSelect backs its value with a hidden input — click the control, not the input
        await page.Locator($".mud-dialog .mud-input-control:has(label:text-is('{selectLabel}'))").ClickAsync();
        await page.Locator($".mud-popover .mud-list-item:has-text('{optionText}')").First.ClickAsync();
    }

    [Fact]
    public async Task RuleCrud_CreateTestToggleEditDelete()
    {
        await SeedProjectAndCleanRulesAsync();

        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync($"{E2EAppFixture.AppUrl}/mapping-rules");

        // ── Create ─────────────────────────────────────────────────────────
        await page.Locator("button:has-text('New Rule')").ClickAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator(".mud-dialog")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await DialogField(page, "Rule Name").FillAsync(RuleName);
        await DialogField(page, "Match Field").FillAsync("ProjectKey");
        await DialogField(page, "Match Value").FillAsync("E2ERULES");
        await SelectMudOptionAsync(page, "Project", "E2E Rules Project");
        await SelectMudOptionAsync(page, "Task (optional)", "E2E Rules Task");

        await page.Locator(".mud-dialog button:has-text('Save')").ClickAsync();
        await Expect(page.Locator(".mud-snackbar")).ToContainTextAsync("Rule saved", new() { Timeout = 15_000 });

        var row = page.Locator($"tr:has-text('{RuleName}')");
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(row).ToContainTextAsync("E2E Rules Project");

        // ── Test (preview) ─────────────────────────────────────────────────
        // Actions cell buttons: [0] test, [1] apply, [2] clone, [3] edit, [4] delete
        var actions = row.Locator("td").Last.Locator("button");
        await actions.Nth(0).ClickAsync();
        await Expect(page.Locator("text=Test Results")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ── Toggle enabled off ─────────────────────────────────────────────
        await row.Locator("input[type='checkbox']").First.SetCheckedAsync(false);
        var disabled = await fixture.WaitForDbAsync(async db =>
            await db.MappingRules.AnyAsync(r => r.Name == RuleName && !r.IsEnabled),
            TimeSpan.FromSeconds(15));
        Assert.True(disabled, "Rule was not disabled in the database");

        // ── Edit (rename) ──────────────────────────────────────────────────
        row = page.Locator($"tr:has-text('{RuleName}')").First;
        await row.Locator("td").Last.Locator("button").Nth(3).ClickAsync();
        await Expect(page.Locator(".mud-dialog:has-text('Edit Rule')")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await DialogField(page, "Rule Name").FillAsync(RenamedRule);
        await page.Locator(".mud-dialog button:has-text('Save')").ClickAsync();
        await Expect(page.Locator($"tr:has-text('{RenamedRule}')")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ── Delete ─────────────────────────────────────────────────────────
        row = page.Locator($"tr:has-text('{RenamedRule}')");
        await row.Locator("td").Last.Locator("button").Nth(4).ClickAsync();
        await page.Locator(".mud-dialog button:has-text('Delete')").ClickAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator($"tr:has-text('{RenamedRule}')")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        await using (var db = fixture.CreateDbContext())
            Assert.False(await db.MappingRules.AnyAsync(r => r.Name.StartsWith("E2E Smoke Rule")));

        await page.CloseAsync();
    }
}
