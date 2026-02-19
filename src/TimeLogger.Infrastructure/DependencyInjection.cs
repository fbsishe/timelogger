using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using TimeLogger.Application.Interfaces;
using TimeLogger.Application.Mapping;
using TimeLogger.Infrastructure.Jira;
using TimeLogger.Infrastructure.Mapping;
using TimeLogger.Infrastructure.Persistence;
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
                client.BaseAddress = new Uri(opts!.BaseUrl);
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        // Jira API
        services.Configure<JiraOptions>(configuration.GetSection(JiraOptions.SectionName));
        services.AddTransient<JiraBasicAuthHandler>();

        services.AddRefitClient<IJiraApiClient>()
            .ConfigureHttpClient((_, client) =>
            {
                var opts = configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>();
                if (opts?.BaseUrl is not null)
                    client.BaseAddress = new Uri(opts.BaseUrl);
            })
            .AddHttpMessageHandler<JiraBasicAuthHandler>();

        // Tempo import — uses IHttpClientFactory for per-source token injection
        services.Configure<TempoOptions>(configuration.GetSection(TempoOptions.SectionName));
        services.AddHttpClient("Tempo");

        // Application services
        services.AddScoped<ITimelogSyncService, TimelogSyncService>();
        services.AddScoped<ITimelogSubmissionService, TimelogSubmissionService>();
        services.AddScoped<ITempoImportService, TempoImportService>();
        services.AddSingleton<IMappingEngine, MappingEngine>();
        services.AddScoped<IApplyMappingsService, ApplyMappingsService>();

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

        RecurringJob.AddOrUpdate<SyncTimelogDataJob>(
            SyncTimelogDataJob.JobId,
            job => job.ExecuteAsync(CancellationToken.None),
            dailyCron);

        RecurringJob.AddOrUpdate<PullTempoWorklogsJob>(
            PullTempoWorklogsJob.JobId,
            job => job.ExecuteAsync(CancellationToken.None),
            dailyCron);
    }
}
