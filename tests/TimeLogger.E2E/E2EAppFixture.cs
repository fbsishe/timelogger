using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TimeLogger.Infrastructure.Persistence;

namespace TimeLogger.E2E;

/// <summary>
/// Boots the full E2E stack once per test collection:
/// a stub Timelog API (Kestrel), the real TimeLogger.Web app (dotnet run against
/// the docker-compose SQL Server), and a Playwright Chromium browser.
/// </summary>
public sealed class E2EAppFixture : IAsyncLifetime
{
    public const string AppUrl = "http://localhost:5599";
    public const string StubUrl = "http://localhost:5598";
    public const string ConnectionString =
        "Server=localhost,14333;Database=TimeLoggerE2E;User Id=sa;Password=E2e_Str0ng!Pass;TrustServerCertificate=True";

    private WebApplication? _stub;
    private Process? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Requests the stub Timelog API received (method + path), for assertions.</summary>
    public List<string> StubRequests { get; } = [];

    public async Task InitializeAsync()
    {
        InstallPlaywrightBrowsers();
        await StartTimelogStubAsync();
        StartApp();
        await WaitForAppHealthyAsync(TimeSpan.FromMinutes(3));

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Polls the database until <paramref name="predicate"/> is satisfied.</summary>
    public async Task<bool> WaitForDbAsync(
        Func<AppDbContext, Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = CreateDbContext();
            if (await predicate(db))
                return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static void InstallPlaywrightBrowsers()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"playwright install chromium failed with exit code {exitCode}");
    }

    private async Task StartTimelogStubAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(StubUrl);
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            lock (StubRequests)
                StubRequests.Add($"{ctx.Request.Method} {ctx.Request.Path}");
            await next();
        });

        // Timelog TAF API surface used by the app — empty lists, successful writes
        var emptyTafList = () => Results.Json(new { Entities = Array.Empty<object>() });
        app.MapPost("/v1/time-registration", () => Results.Ok(new { }));
        app.MapPut("/v1/time-registration", () => Results.Ok(new { }));
        app.MapDelete("/v1/time-registration/{id:int}", (int id) => Results.Ok(new { }));
        app.MapGet("/v1/{**rest}", emptyTafList);
        app.MapFallback(() => Results.Ok(new { }));

        await app.StartAsync();
        _stub = app;
    }

    private void StartApp()
    {
        var webProject = Path.Combine(FindRepoRoot(), "src", "TimeLogger.Web");

        // --no-launch-profile: launchSettings.json would override ASPNETCORE_URLS
        var psi = new ProcessStartInfo("dotnet", "run --no-build --no-launch-profile")
        {
            WorkingDirectory = webProject,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = AppUrl;
        psi.Environment["ConnectionStrings__Default"] = ConnectionString;
        psi.Environment["Timelog__BaseUrl"] = StubUrl;
        psi.Environment["Timelog__ApiKey"] = "e2e-key";
        psi.Environment["Timelog__SubmitRetryBaseDelaySeconds"] = "0";
        psi.Environment["Tempo__BaseUrl"] = StubUrl;
        psi.Environment["Jira__BaseUrl"] = StubUrl;
        psi.Environment["Jira__Email"] = "e2e@localhost";
        psi.Environment["Jira__ApiToken"] = "e2e-token";
        // Feb 29 — effectively never fires during a test run
        psi.Environment["Hangfire__DailyPullCron"] = "0 0 29 2 *";

        _app = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start TimeLogger.Web");

        // Drain output so the process doesn't block on full pipes
        _app.OutputDataReceived += (_, _) => { };
        _app.ErrorDataReceived += (_, _) => { };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();
    }

    private async Task WaitForAppHealthyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_app is { HasExited: true })
                throw new InvalidOperationException($"TimeLogger.Web exited early with code {_app.ExitCode}");

            try
            {
                var response = await http.GetAsync($"{AppUrl}/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"TimeLogger.Web did not become healthy at {AppUrl}/health within {timeout}.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TimeLogger.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root (TimeLogger.sln).");
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            await _app.WaitForExitAsync();
        }
        _app?.Dispose();

        if (_stub is not null)
            await _stub.DisposeAsync();
    }
}

[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<E2EAppFixture>;
