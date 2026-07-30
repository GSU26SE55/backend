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
using SharedInfrastructure.Idempotency;

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
        services.AddInboxIdempotency(configuration);

        services.AddHostedService<BackgroundJobs.NotificationAuditOutboxRelayBackgroundService>(); // Sprint audit #AUDIT-34

        // Sprint 6.2 NOTI-01 (#672) — worker giao record Pending xuống channel (Push/Email/SMS/InApp).
        // Không có nó thì dispatcher vẫn là dead code và notification chỉ nằm trong DB.
        services.Configure<NotificationDispatchOptions>(
            configuration.GetSection(NotificationDispatchOptions.SectionName));
        services.AddHostedService<BackgroundJobs.NotificationDispatchBackgroundService>();

        // Sprint 6.2 NOTI-12 (#683) — gom digest cho user bật Frequency=Daily / DigestWindowMinutes.
        services.Configure<NotificationDigestOptions>(
            configuration.GetSection(NotificationDigestOptions.SectionName));
        services.AddHostedService<BackgroundJobs.NotificationDigestBackgroundService>();

        // Sprint 6.3 NOTI3-08 (#708) — đo độ sâu dead-letter queue qua RabbitMQ Management API.
        // MassTransit không expose API đọc queue depth nên phải hỏi trực tiếp broker.
        services.AddHttpClient("rabbitmq-management", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHostedService<BackgroundJobs.NotificationDlqMonitorBackgroundService>();

        // Sprint 6.3 NOTI3-02 (#702) — đối soát biên nhận Expo.
        // Ticket "ok" chỉ chứng minh Expo NHẬN request; giao hàng thật phải hỏi /push/getReceipts.
        services.Configure<ExpoReceiptOptions>(
            configuration.GetSection(ExpoReceiptOptions.SectionName));
        services.AddHostedService<BackgroundJobs.ExpoReceiptReconcileBackgroundService>();

        // Sprint 6.3 NOTI3-05 (#705) — bù SMS khi push critical không có receipt.
        // Phụ thuộc dữ liệu receipt của NOTI3-02: tắt đối soát thì fallback cũng vô nghĩa.
        services.Configure<NotificationFallbackOptions>(
            configuration.GetSection(NotificationFallbackOptions.SectionName));
        services.AddHostedService<BackgroundJobs.NotificationFallbackBackgroundService>();

        // Sprint 6.3 NOTI3-06 (#706) — hạn mức per-user. Vượt trần thì hoãn vào digest, không vứt.
        services.Configure<NotificationRateLimitOptions>(
            configuration.GetSection(NotificationRateLimitOptions.SectionName));
        services.AddScoped<INotificationRateLimiter, Services.NotificationRateLimiter>();

        // Sprint 6.3 NOTI3-11 (#711) — dọn notification quá hạn hằng đêm (giữ critical vĩnh viễn).
        services.Configure<NotificationRetentionOptions>(
            configuration.GetSection(NotificationRetentionOptions.SectionName));
        services.AddHostedService<BackgroundJobs.NotificationRetentionBackgroundService>();

        // Sprint 6.3 NOTI3-15 (#715) — token ký HMAC cho link hủy đăng ký một chạm.
        services.AddSingleton<UnsubscribeTokenService>();

        // Sprint 6.3 NOTI3-13 (#713) — đẩy notification mới + badge count qua SignalR.
        services.AddScoped<INotificationRealtimeNotifier, Realtime.SignalRNotificationNotifier>();

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
        services.AddScoped<IRecipientResolver, RecipientResolver>();
        services.AddScoped<INotificationAuditWriter, NotificationAuditWriter>(); // Sprint 6.2 NOTI-13 (#684)
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
