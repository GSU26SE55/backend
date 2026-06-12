using BatteryService.Application.Anomaly;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.Infrastructure.Consumers;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.Infrastructure.Persistence;
using BatteryService.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddBatteryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScoped<IBatteryUnitOfWork, UnitOfWork>();
        services.AddScoped<BatteryDataSeeder>();
        services.AddScoped<EnvironmentDataSeeder>();
        services.AddSharedInfrastructure(configuration, "BatteryService.Application", "Battery Service API");
        services.AddMessageBus(configuration, typeof(AccountActivatedConsumer).Assembly);
        services.AddInboxIdempotency(configuration);

        // Anomaly engine config (Sprint 3) — service/AnomalyRules dùng options này
        services.Configure<AnomalyEngineOptions>(configuration.GetSection(AnomalyEngineOptions.SectionName));

        // Background-only services — CQRS không cần thiết vì không expose qua REST.
        // Background worker chỉ làm cron trigger, logic ở service này.
        services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
        services.AddScoped<IAlertEscalationService, AlertEscalationService>();
        services.AddScoped<IAlertAutoResolveService, AlertAutoResolveService>();
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        services.AddScoped<SharedContracts.Interfaces.IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();

        services.AddHostedService<ThresholdCheckBackgroundService>();
        services.AddHostedService<AlertEscalationBackgroundService>();
        services.AddHostedService<AlertAutoResolveBackgroundService>();
        services.AddHostedService<OutboxRelayBackgroundService>();

        // Sprint 5B #90/#92 — Open-Meteo client + WeatherSync.
        services.Configure<WeatherSyncOptions>(configuration.GetSection(WeatherSyncOptions.SectionName));
        services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WeatherSyncOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opt.HttpTimeoutSeconds));
            })
            .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
                .OrResult(r => (int)r.StatusCode == 429)
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))));
        services.AddHostedService<WeatherSyncBackgroundService>();

        // Sprint 5B B1 (#152) — NoiseBreachEvent retention 7 ngày.
        services.AddHostedService<NoiseBreachRetentionBackgroundService>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BatteryDb")
                               ?? configuration["BatteryDb"]
                               ?? configuration["Battery_Db"]
                               ?? configuration["BATTERY_DB"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing Battery database connection string. Expected ConnectionStrings__BatteryDb, BatteryDb, Battery_Db, or BATTERY_DB.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
    }
}
