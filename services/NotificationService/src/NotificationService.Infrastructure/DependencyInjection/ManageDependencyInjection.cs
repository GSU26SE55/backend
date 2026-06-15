using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Infrastructure.Implements.Repositories;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Seeders;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;

namespace NotificationService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddNotificationServiceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScopedInterface();
        services.AddSharedInfrastructure(configuration, "NotificationService.Application", "Notification Service API");

        // MassTransit consumers — Sprint IoT-1 (#249) đăng ký consumer assembly để consume
        // IotDeviceWentOfflineEvent (và sẵn sàng cho các consumer Sprint 6 khác trong cùng assembly).
        services.AddMessageBus(configuration, typeof(NotificationService.Application.Consumers.IotDeviceWentOfflineConsumer).Assembly);

        services.AddScoped<NotificationDataSeeder>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NotificationDb")
                               ?? configuration["NotificationDb"]
                               ?? configuration["Notification_Db"]
                               ?? configuration["NOTIFICATION_DB"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__NotificationDb, NotificationDb, Notification_Db, or NOTIFICATION_DB.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetService<ApplicationDbContext>()!);
    }

    private static void AddScopedInterface(this IServiceCollection services)
    {
        services.AddScoped<INotificationUnitOfWork, UnitOfWork>();
        services.AddHttpContextAccessor();
    }
}
