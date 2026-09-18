using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Mapping;
using TimeLogger.Application.Services;
using TimeLogger.Infrastructure.FileImport;
using TimeLogger.Infrastructure.Jira;
using TimeLogger.Infrastructure.Jobs;
using TimeLogger.Infrastructure.Mapping;
using TimeLogger.Infrastructure.Persistence;
using TimeLogger.Infrastructure.Services;
using TimeLogger.Infrastructure.Tempo;
using TimeLogger.Infrastructure.Timelog;

namespace TimeLogger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // Timelog API
        services.Configure<TimelogOptions>(configuration.GetSection(TimelogOptions.SectionName));
        services.AddTransient<BearerTokenHandler>();

        services.AddRefitClient<ITimelogApiClient>()
            .ConfigureHttpClient((_, client) =>
            {
                var opts = configuration.GetSection(TimelogOptions.SectionName).Get<TimelogOptions>();
                if (Uri.TryCreate(opts?.BaseUrl, UriKind.Absolute, out var timelogUri))
                    client.BaseAddress = timelogUri;
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        // Jira API
        services.Configure<JiraOptions>(configuration.GetSection(JiraOptions.SectionName));
        services.AddTransient<JiraBasicAuthHandler>();

        services.AddRefitClient<IJiraApiClient>()
            .ConfigureHttpClient((_, client) =>
            {
                var opts = configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>();
                if (Uri.TryCreate(opts?.BaseUrl, UriKind.Absolute, out var jiraUri))
                    client.BaseAddress = jiraUri;
            })
            .AddHttpMessageHandler<JiraBasicAuthHandler>();

        // Tempo import — uses IHttpClientFactory for per-source token injection
        services.Configure<TempoOptions>(configuration.GetSection(TempoOptions.SectionName));
        services.AddHttpClient("Tempo");

        // Application services
        services.AddScoped<ITimelogSyncService, TimelogSyncService>();
        services.AddScoped<ITimelogSubmissionService, TimelogSubmissionService>();
        services.AddScoped<IDayReviewService, DayReviewService>();
        services.Configure<TimelogReportingOptions>(configuration.GetSection(TimelogReportingOptions.SectionName));
        services.AddHttpClient(TimelogReportingClient.HttpClientName);
        services.AddScoped<ITimelogReportingClient, TimelogReportingClient>();
        services.AddScoped<ITempoImportService, TempoImportService>();
        services.AddSingleton<IMappingEngine>(_ => new MappingEngine(
            configuration["Mapping:OvertimeAttributeKey"] ?? MappingEngine.DefaultOvertimeAttributeKey));
        services.AddScoped<IApplyMappingsService, ApplyMappingsService>();
        services.Configure<MappingSuggestionOptions>(configuration.GetSection(MappingSuggestionOptions.SectionName));
        services.AddScoped<IMappingSuggestionService, MappingSuggestionService>();

        // UI application services
        services.AddScoped<IEntryService, EntryService>();
        services.AddScoped<IMappingRuleService, MappingRuleService>();
        services.AddScoped<IImportSourceService, ImportSourceService>();
        services.AddScoped<ITimelogDataService, TimelogDataService>();
        services.AddScoped<IFileImportService, FileImportService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IEmployeeMappingService, EmployeeMappingService>();
        services.AddScoped<IAppUserService, AppUserService>();

        // Audit log — Web replaces the fallback provider with the circuit-aware one
        services.AddScoped<ICurrentUserProvider, SystemCurrentUserProvider>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Job health monitoring & failure notifications
        services.Configure<JobHealthOptions>(configuration.GetSection(JobHealthOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.AddHttpClient("Notifications");
        services.AddScoped<IJobHealthService, JobHealthService>();
        services.AddScoped<IJobFailureNotifier, JobFailureNotifier>();

        // Scheduled auto-submission with Slack report
        services.Configure<AutoSubmitOptions>(configuration.GetSection(AutoSubmitOptions.SectionName));
        services.AddScoped<ISlackMessageSender, SlackWebhookMessageSender>();

        // Hangfire
        var connectionString = configuration.GetConnectionString("Default")!;
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));

        services.AddHangfireServer();

        return services;
    }

    /// <summary>
    /// Registers Hangfire recurring jobs. Call this after app.UseHangfireDashboard() in Program.cs.
    /// </summary>
    public static void AddRecurringJobs(IConfiguration configuration)
    {
        var dailyCron = configuration["Hangfire:DailyPullCron"] ?? Cron.Daily();

        var autoSubmit = configuration.GetSection(AutoSubmitOptions.SectionName).Get<AutoSubmitOptions>()
            ?? new AutoSubmitOptions();

        // Every recurring job shares one wall clock — the office's, from AutoSubmit:TimeZone.
        // Without this, an unqualified cron is read as UTC and "06:00" silently drifts with
        // the seasons, landing after the 08:00 report run for half the year.
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(autoSubmit.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localTime = new RecurringJobOptions { TimeZone = timeZone };

        RecurringJob.AddOrUpdate<SyncTimelogDataJob>(
            SyncTimelogDataJob.JobId,
            job => job.ExecuteAsync(CancellationToken.None),
            dailyCron,
            localTime);

        RecurringJob.AddOrUpdate<PullTempoWorklogsJob>(
            PullTempoWorklogsJob.JobId,
            job => job.ExecuteAsync(CancellationToken.None),
            dailyCron,
            localTime);

        // Scheduled auto-submission (TL-99) — opt-in via AutoSubmit:Enabled. It runs the
        // pull itself as its first step, so entries logged during the day reach Timelog on
        // the same day instead of waiting for the next daily pull.
        // Manual submission from the UI stays available either way.
        if (autoSubmit.Enabled)
        {
            RecurringJob.AddOrUpdate<AutoSubmitReportJob>(
                AutoSubmitReportJob.JobId,
                job => job.ExecuteAsync(CancellationToken.None),
                autoSubmit.Cron,
                localTime);
        }
        else
        {
            RecurringJob.RemoveIfExists(AutoSubmitReportJob.JobId);
        }
    }
}
