using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Implements.Repositories;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Seeders;
using NotificationService.Infrastructure.Services;
using Polly;
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
        services.AddNotificationChannels();

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
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddSingleton<ITemplateRenderer, HandlebarsTemplateRenderer>();
        services.AddHttpContextAccessor();
    }

    private static void AddNotificationChannels(this IServiceCollection services)
    {
        // Named HttpClient "expo" với Polly retry 3 lần exponential backoff
        services.AddHttpClient("expo", c => { c.Timeout = TimeSpan.FromSeconds(30); })
                .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
                    3,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))));

        services.AddScoped<INotificationChannel, ExpoPushChannel>();
        services.AddScoped<INotificationChannel, EmailBusChannel>();
        services.AddScoped<INotificationChannel, SmsBusChannel>();
        services.AddScoped<INotificationChannel, InAppChannel>();
    }
}
