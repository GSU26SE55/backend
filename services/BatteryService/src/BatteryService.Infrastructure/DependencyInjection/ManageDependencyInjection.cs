using BatteryService.Application.Interfaces;
using BatteryService.Infrastructure.Consumers;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using BatteryService.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSharedInfrastructure(configuration, "BatteryService.Application", "Battery Service API");
        services.AddMessageBus(configuration, typeof(AccountActivatedConsumer).Assembly);
        services.AddInboxIdempotency(configuration);
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
