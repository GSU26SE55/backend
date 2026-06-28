using AuditAggregatorService.Application.Consumers;
using AuditAggregatorService.Application.DependencyInjection;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Infrastructure.Implements;
using AuditAggregatorService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;

namespace AuditAggregatorService.Infrastructure.DependencyInjection;

/// <summary>
/// DI cho AuditAggregatorService (Sprint audit <c>#AUDIT-13</c>) — model theo các service hiện có.
/// </summary>
public static class ManageDependencyInjection
{
    public static IServiceCollection AddAuditAggregatorServiceInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScopedInterface();
        services.AddAuditAggregatorApplication();   // CQRS — MediatR handlers (#AUDIT-17), theo pattern các service khác.
        services.AddSharedInfrastructure(configuration, "AuditAggregatorService.Application", "Audit Aggregator Service API");

        // MassTransit — consume AuditCreatedEventV1 từ exchange audit.events (#AUDIT-15).
        services.AddMessageBus(configuration, typeof(AuditCreatedConsumer).Assembly);

        // #AUDIT-41 — retention daily drop row > 6 tháng (trừ Critical/Security).
        services.AddHostedService<BackgroundJobs.AuditRetentionBackgroundService>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuditAggregatorDb")
                               ?? configuration["AuditAggregatorDb"]
                               ?? configuration["AUDIT_AGGREGATOR_DB"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__AuditAggregatorDb, AuditAggregatorDb, or AUDIT_AGGREGATOR_DB.");

        services.AddDbContext<AuditAggregateDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            // AuditableEntityInterceptor (đăng ký bởi AddSharedInfrastructure) — set CreatedAt/CreatedBy lúc insert,
            // nhất quán các service khác. Redact/retention dùng ExecuteUpdate/Delete (bulk) nên không qua interceptor.
            options.AddInterceptors(sp.GetRequiredService<SharedInfrastructure.Persistence.Interceptors.AuditableEntityInterceptor>());
        });
        services.AddScoped<DbContext>(provider => provider.GetService<AuditAggregateDbContext>()!);
    }

    private static void AddScopedInterface(this IServiceCollection services)
    {
        // UnitOfWork (Shared IGenericRepository) — theo chuẩn các service khác. KHÔNG dùng custom repository nữa.
        services.AddScoped<IAuditAggregatorUnitOfWork, Implements.Repositories.UnitOfWork>();

        // #AUDIT-16 — Geo IP: singleton (DatabaseReader thread-safe) + LRU cache 10k entry.
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddSingleton<IGeoIpResolver, MaxMindGeoIpResolver>();

        services.AddHttpContextAccessor();
    }
}
