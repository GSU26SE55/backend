using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Services;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.BackgroundServices;
using TicketService.Infrastructure.Caching;
using TicketService.Infrastructure.Implements.Repositories;
using TicketService.Infrastructure.Implements.Services;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Realtime;
using TicketService.Infrastructure.Sagas;

namespace TicketService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddRepositories();
        services.AddHelpers();
        services.AddOptions<SlaBusinessHoursOptions>()
            .Bind(configuration.GetSection(SlaBusinessHoursOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.TimeZoneId)
                                 && SlaBusinessHoursOptions.IsValidTimeZone(options.TimeZoneId),
                "SlaBusinessHours:TimeZoneId must be a valid time zone identifier.")
            .Validate(options => options.Start >= TimeSpan.Zero
                                 && options.End <= TimeSpan.FromDays(1)
                                 && options.Start < options.End,
                "SlaBusinessHours must define a non-empty interval within one day.")
            .Validate(options => options.WorkingDays is { Length: > 0 }
                                 && options.WorkingDays.All(Enum.IsDefined),
                "SlaBusinessHours:WorkingDays must contain valid working days.")
            .Validate(options => options.Start == TimeSpan.FromHours(7)
                                 && options.End == TimeSpan.FromHours(17)
                                 && options.WorkingDays.Distinct().Count() == 7,
                "SlaBusinessHours must be 07:00-17:00 on all seven days.")
            .ValidateOnStart();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddOptions<TicketScheduleOptions>()
            .Bind(configuration.GetSection(TicketScheduleOptions.SectionName))
            .Validate(options => options.CurrentWindowMinutes > 0, "Ticket:Schedule:CurrentWindowMinutes must be greater than zero.")
            .Validate(options => options.PollIntervalSeconds > 0, "Ticket:Schedule:PollIntervalSeconds must be greater than zero.")
            .Validate(options => options.BatchSize > 0, "Ticket:Schedule:BatchSize must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<PeriodicMaintenanceOptions>()
            .Bind(configuration.GetSection(PeriodicMaintenanceOptions.SectionName))
            .Validate(options => options.OverdueScheduleWindowDays > 0,
                "Ticket:PeriodicMaintenance:OverdueScheduleWindowDays must be greater than zero.")
            .Validate(options => options.PollIntervalSeconds > 0,
                "Ticket:PeriodicMaintenance:PollIntervalSeconds must be greater than zero.")
            .Validate(options => options.BatchSize > 0,
                "Ticket:PeriodicMaintenance:BatchSize must be greater than zero.")
            .Validate(options => options.ReminderTime >= TimeSpan.Zero && options.ReminderTime < TimeSpan.FromDays(1),
                "Ticket:PeriodicMaintenance:ReminderTime must be within one local day.")
            .Validate(options =>
            {
                try
                {
                    _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
                    return true;
                }
                catch (TimeZoneNotFoundException)
                {
                    return false;
                }
                catch (InvalidTimeZoneException)
                {
                    return false;
                }
            }, "Ticket:PeriodicMaintenance:TimeZoneId is invalid.")
            .ValidateOnStart();
        services.AddAiVerify(configuration);
        services.AddOutbox(configuration);

        services.AddSharedInfrastructure(configuration, "TicketService.Application", "Ticket Service API");
        services.AddInboxIdempotency(configuration);
        services.AddIdempotencyKey(configuration);

        // Sprint 5B #237 — Quartz cluster persistent store (cho Saga timeout).
        services.AddAlertTicketSaga(configuration);

        // Sprint 5B #237/#238 + #566 — add Sagas + consumers vào MassTransit bus.
        // FIX duplicate-ticket — khi Saga bật, consumer cũ TicketBatteryAnomalyDetectedConsumer
        // Consumer fallback #238 PHẢI không được đăng ký, nếu không cả 2 cùng tạo ticket từ 1 alert
        // (gây trùng mã ticket → 23505 duplicate key IX_tickets_code).
        var sagaEnabled = configuration.GetValue(
            $"{AlertTicketSagaOptions.SectionName}:{nameof(AlertTicketSagaOptions.AlertTicketSagaEnabled)}",
            true);
        var excludedConsumers = sagaEnabled
            ? new[] { typeof(Consumers.TicketBatteryAnomalyDetectedConsumer) }
            : Array.Empty<Type>();

        services.AddMessageBus(
            configuration,
            configure: bus =>
            {
                SagaServiceCollectionExtensions.ConfigureAlertTicketSaga(bus);
                SagaServiceCollectionExtensions.ConfigureChatEscalationReviewSaga(bus);
            },
            // FIX saga-scheduler — bật UseMessageScheduler cho saga timer (.Schedule/.Unschedule).
            configureBus: SagaServiceCollectionExtensions.ConfigureAlertTicketSagaBus,
            excludedConsumerTypes: excludedConsumers,
            typeof(ManageDependencyInjection).Assembly,
            typeof(TicketService.Application.DependencyInjection.ManageDependencyInjection).Assembly);

        // Command handlers write through IIntegrationEventOutboxWriter. The relay uses
        // IIntegrationEventTransport to publish to RabbitMQ after the transaction commits.

        // Sprint 5B #238 — feature flag override cho cutover.
        services.Configure<AlertTicketSagaOptions>(configuration.GetSection(AlertTicketSagaOptions.SectionName));

        return services;
    }

    private static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(options => options.IntervalSeconds > 0, "Outbox:IntervalSeconds must be greater than 0.")
            .Validate(options => options.BatchSize > 0, "Outbox:BatchSize must be greater than 0.")
            .Validate(options => options.MaxRetryCount > 0, "Outbox:MaxRetryCount must be greater than 0.")
            .Validate(options => options.PublishTimeoutSeconds > 0, "Outbox:PublishTimeoutSeconds must be greater than 0.")
            .Validate(options => options.LeaseDurationSeconds >= options.PublishTimeoutSeconds + 5,
                "Outbox:LeaseDurationSeconds must exceed PublishTimeoutSeconds by at least a 5-second safety buffer.")
            .ValidateOnStart();
        services.AddScoped<IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        services.AddScoped<IOutboxClaimService, OutboxClaimService>();
        services.AddSingleton<IOutboxLeaseOwner, OutboxLeaseOwner>();
        services.AddScoped<IAlertTicketSagaQueryService, AlertTicketSagaQueryService>();
        // Sprint 7 #114 (§5.2) — saga failed-rate report reader.
        services.AddScoped<TicketService.Application.Interfaces.Services.ISagaReportService,
            TicketService.Infrastructure.Implements.Services.SagaReportService>();
        // Sprint 7 #117 — SLA aggregate gauge (cho Grafana "SLA Ops").
        services.AddSingleton<TicketService.Application.Interfaces.Services.ISlaMetricsRecorder,
            TicketService.Infrastructure.Observability.SlaMetricsRecorder>();
        services.AddHostedService<OutboxRelayBackgroundService>();
        services.AddHostedService<SlaTimerBackgroundService>();
        services.AddHostedService<TicketScheduleActivationBackgroundService>();
        // Nhắc khách chọn giờ cho ticket bảo trì định kỳ, và bàn lại cho Manager
        // khi khách im lặng qua ba mốc.
        services.AddHostedService<BackgroundJobs.PeriodicMaintenanceReminderBackgroundService>();


        // Read receipt — channel-based bulk writer (#541/#542)
        services.AddSingleton<IChatReadReceiptQueue, ChatReadReceiptQueue>();
        services.AddHostedService<ChatReadReceiptBulkWriter>();

        // #569 — GDPR retention: daily archive chats older than Chat:Retention:ArchiveAfterYears
        services.AddHostedService<ChatRetentionService>();

        // #514 — VirusScan worker (disabled by default via Chat:Features:EnableVirusScan=false)
        services.AddHostedService<VirusScanWorker>();
        services.AddHostedService<SlaGaugeBackgroundService>();
        services.AddHostedService<BackgroundJobs.TicketAuditOutboxRelayBackgroundService>(); // Sprint audit #AUDIT-25

        // Remind Customers to rate eligible unrated Closed tickets during the grace period.
        services.AddHostedService<BackgroundJobs.RatingRequestBackgroundService>();
    }

    private static void AddHelpers(this IServiceCollection services)
    {
        services.AddScoped<IPriorityCalculator, PriorityCalculator>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITicketCodeGenerator, TicketCodeGenerator>();
        services.AddScoped<IKbCodeGenerator, KbCodeGenerator>();
        services.AddScoped<ISlaCalculator, SlaCalculator>();
        services.AddScoped<ISlaBusinessCalendarProvider, SlaBusinessCalendarProvider>();
        services.AddScoped<ISlaDeadlineReconciler, SlaDeadlineReconciler>();
        services.AddScoped<ISlaService, SlaService>();

        // Override CurrentUserService from Shared
        services.AddScoped<TicketCurrentUserService>();
        services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());
        services.AddScoped<ITicketCurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());

        // Realtime Chat Services
        services.AddScoped<IChatAuthorizationService, ChatAuthorizationService>();
        services.AddScoped<IChatRecipientResolver, ChatRecipientResolver>();
        services.AddScoped<ITicketChatRealtimeNotifier, SignalRTicketChatNotifier>();
        services.AddScoped<IMarkdownRenderer, MarkdigMarkdownRenderer>();

        // Chat Authorization + Validation (#518/#519)
        services.AddScoped<ISpamDetector, SpamDetector>();
        services.AddScoped<IProfanityFilter, ProfanityFilter>();
        services.AddScoped<IPiiDetector, PiiDetector>();  // PiiDetector giờ inject ICacheService (#559)
        services.AddSingleton<ITechnicalTermMasker, TechnicalTermMasker>();

        // Group mention resolver (#537)
        services.AddScoped<IGroupMentionResolverService, LocalGroupMentionResolver>();

        // #557 — User connection tracker (Singleton vì in-memory, stateful across requests)
        services.AddSingleton<IUserConnectionTracker, InMemoryUserConnectionTracker>();

        // #633 — Local language detector (Singleton: Lingua models loaded once per process)
        services.AddSingleton<ILanguageDetectionService, LinguaLanguageDetectionService>();

        // #552 — Chat cache service
        services.AddScoped<IChatCacheService, ChatCacheService>();

        // #547 — Template renderer
        services.AddScoped<ITemplateRenderer, TemplateRendererService>();

        // #564 — KB suggestion service
        services.AddScoped<IKbSuggestionService, KbSuggestionService>();

        // GH-671 — Blog AI generator (reuses DeepSeekChatAiClient HttpClient)
        services.AddScoped<IBlogGeneratorService, DeepSeekBlogGeneratorService>();

        // #568 — PDF exporter

        // Voice m4a normalization — transcode file voice bất kỳ (web ghi .webm) về m4a để iOS phát được
        services.AddScoped<IAudioTranscoder, FfmpegAudioTranscoder>();

        // #559/#560 — DeepSeek implementations
        services.AddHttpClient<DeepSeekChatAiClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.DeepSeek.TimeoutSeconds));
        });
        // DeepSeekChatTextAiClient tái sử dụng HttpClient của DeepSeekChatAiClient qua inner client
        services.AddTransient<DeepSeekChatTextAiClient>();

        // #633 — DeepSeek is the sole text AI provider (Gemini removed); voice uses GeminiVoiceTranscriptionService
        services.AddScoped<IChatAiSuggestionClient>(sp => sp.GetRequiredService<DeepSeekChatAiClient>());
        services.AddScoped<IChatTextAiClient>(sp => sp.GetRequiredService<DeepSeekChatTextAiClient>());

        // #514 — ClamAV REST client (typed HttpClient)
        services.AddHttpClient<IClamAvClient, ClamAvHttpClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.BaseAddress = new Uri(opts.VirusScan.Endpoint);
            http.Timeout = TimeSpan.FromSeconds(Math.Max(10, opts.VirusScan.TimeoutSeconds));
        });

        // GH-790 — đã BỎ named HttpClient "FileDownload".
        // Nó gọi GET /api/files/{id}/download mà không gắn token, trong khi endpoint đó có
        // [Authorize] ⇒ mọi lần tải đều 401. Việc tải file để quét virus đã chuyển sang kênh gRPC
        // nội bộ FileInternal (đăng ký ngay bên dưới, dùng chung với voice transcription).
        // Giữ lại registration này chỉ tạo ra một đường chết mà người sau tưởng là đang dùng.

        // #567 — Gemini voice transcription client (multimodal, timeout từ Chat:Voice:TranscribeTimeoutSeconds)
        services.AddHttpClient<IVoiceTranscriptionService, GeminiVoiceTranscriptionService>((sp, http) =>
               {
                   var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
                   http.Timeout = TimeSpan.FromSeconds(Math.Max(15, opts.Voice.TranscribeTimeoutSeconds));
               });

        services.AddGrpcClient<SharedContracts.Grpc.FileInternal.FileInternal.FileInternalClient>((sp, options) =>
        {
            var address = sp.GetRequiredService<IConfiguration>()["FILE_STORAGE_GRPC_CLIENT_ADDRESS"]
                ?? sp.GetRequiredService<IOptions<ChatOptions>>().Value.Voice.FileStorageGrpcAddress;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Chat:Voice:FileStorageGrpcAddress must be an absolute URI.");
            options.Address = uri;
        });

        // #567 — FileStorageService upload client (dùng Bearer token forwarded từ original request)
        services.AddScoped<ITicketActivationService, TicketActivationService>();
    }

    /// <summary>
    /// GH-ticket-verify — battery serial lookup (HTTP, JWT forward) + AI verify (gRPC).
    /// </summary>
    private static void AddAiVerify(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TicketAiOptions>(configuration.GetSection(TicketAiOptions.SectionName));
        var aiOptions = configuration.GetSection(TicketAiOptions.SectionName).Get<TicketAiOptions>() ?? new TicketAiOptions();

        // Battery serial lookup — typed HttpClient, base url = BatteryService (JWT forward trong client).
        services.AddHttpClient<IBatteryLookupClient, BatteryLookupHttpClient>((_, http) =>
        {
            http.BaseAddress = new Uri(aiOptions.BatteryServiceBaseUrl);
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, aiOptions.TimeoutSeconds));
        });

        // gRPC AiService client (VerifyTicket) — insecure, nội bộ docker network.
        services.AddGrpcClient<AiModule.V1.AiService.AiServiceClient>(o =>
        {
            o.Address = new Uri(aiOptions.AiGrpcAddress);
        });
        services.AddScoped<IAiTicketVerifyClient>(sp => new AiTicketVerifyGrpcClient(
            sp.GetRequiredService<AiModule.V1.AiService.AiServiceClient>(),
            sp.GetRequiredService<ILogger<AiTicketVerifyGrpcClient>>(),
            aiOptions.TimeoutSeconds));

        // Gợi ý staff + KB — dùng chung AiServiceClient ở trên. Fail-safe: client trả null
        // khi AI không phản hồi, endpoint trả danh sách rỗng kèm cờ AiAvailable=false.
        services.AddScoped<IAiStaffSuggestClient>(sp => new AiStaffSuggestGrpcClient(
            sp.GetRequiredService<AiModule.V1.AiService.AiServiceClient>(),
            sp.GetRequiredService<ILogger<AiStaffSuggestGrpcClient>>(),
            aiOptions.TimeoutSeconds));
        services.AddScoped<IAiKbSuggestClient>(sp => new AiKbSuggestGrpcClient(
            sp.GetRequiredService<AiModule.V1.AiService.AiServiceClient>(),
            sp.GetRequiredService<ILogger<AiKbSuggestGrpcClient>>(),
            aiOptions.TimeoutSeconds));

        // GH-verify-sensor-grpc — gRPC BatteryService client (đọc sensor pin verify), nội bộ, không JWT.
        services.AddGrpcClient<BatteryService.Grpc.BatteryInternal.BatteryInternalClient>(o =>
        {
            o.Address = new Uri(aiOptions.BatteryGrpcAddress);
        });
        services.AddScoped<IBatterySensorClient>(sp => new BatterySensorGrpcClient(
            sp.GetRequiredService<BatteryService.Grpc.BatteryInternal.BatteryInternalClient>(),
            sp.GetRequiredService<ILogger<BatterySensorGrpcClient>>(),
            aiOptions.TimeoutSeconds));

        // Logic verify dùng chung consumer (async) + re-verify thủ công (đồng bộ).
        services.AddScoped<ITicketVerifyRunner, TicketVerifyRunner>();
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TicketDb")
                               ?? configuration["TicketDb"]
                               ?? configuration["Ticket_Db"]
                               ?? configuration["Ticket_DB"]
                               ?? configuration["TICKET_DB"]
                               ?? configuration["TICKET_Db"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__TicketDb, TicketDb, Ticket_Db, Ticket_DB, TICKET_DB, or TICKET_Db.");

        services.AddDbContext<TicketDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetService<TicketDbContext>()!);
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ITicketUnitOfWork, UnitOfWork>();
        services.AddHttpContextAccessor();
    }
}
