using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Services;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Services;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.BackgroundServices;
using TicketService.Infrastructure.Caching;
using TicketService.Infrastructure.Implements.Helpers;
using TicketService.Infrastructure.Implements.Repositories;
using TicketService.Infrastructure.Implements.Services;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Persistence.Seeders;
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
        services.AddOutbox(configuration);

        services.AddSharedInfrastructure(configuration, "TicketService.Application", "Ticket Service API");
        services.AddInboxIdempotency(configuration);
        services.AddIdempotencyKey(configuration);

        // Sprint 5B #237 — Quartz cluster persistent store (cho Saga timeout).
        services.AddAlertTicketSaga(configuration);

        // Sprint 5B #237/#238 + #566 — add Sagas + consumers vào MassTransit bus.
        services.AddMessageBus(
            configuration,
            configure: bus =>
            {
                SagaServiceCollectionExtensions.ConfigureAlertTicketSaga(bus);
                SagaServiceCollectionExtensions.ConfigureChatEscalationReviewSaga(bus);
            },
            typeof(ManageDependencyInjection).Assembly,
            typeof(TicketService.Application.DependencyInjection.ManageDependencyInjection).Assembly);

        // Sprint 5B #238 — feature flag override cho cutover.
        services.Configure<AlertTicketSagaOptions>(configuration.GetSection(AlertTicketSagaOptions.SectionName));

        return services;
    }

    private static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IMessageProducerService, OutboxMessagePublisher>();
        services.AddScoped<IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        services.AddScoped<IAlertTicketSagaQueryService, AlertTicketSagaQueryService>();
        // Sprint 7 #114 (§5.2) — saga failed-rate report reader.
        services.AddScoped<TicketService.Application.Interfaces.Services.ISagaReportService,
            TicketService.Infrastructure.Implements.Services.SagaReportService>();
        // Sprint 7 #117 — SLA aggregate gauge (cho Grafana "SLA Ops").
        services.AddSingleton<TicketService.Application.Interfaces.Services.ISlaMetricsRecorder,
            TicketService.Infrastructure.Observability.SlaMetricsRecorder>();
        services.AddHostedService<OutboxRelayBackgroundService>();
        services.AddHostedService<SlaTimerBackgroundService>();

        // Read receipt — channel-based bulk writer (#541/#542)
        services.AddSingleton<IChatReadReceiptQueue, ChatReadReceiptQueue>();
        services.AddHostedService<ChatReadReceiptBulkWriter>();

        // #569 — GDPR retention: daily archive chats older than Chat:Retention:ArchiveAfterYears
        services.AddHostedService<ChatRetentionService>();

        // #514 — VirusScan worker (disabled by default via Chat:Features:EnableVirusScan=false)
        services.AddHostedService<VirusScanWorker>();
        services.AddHostedService<SlaGaugeBackgroundService>();
        services.AddHostedService<BackgroundJobs.TicketAuditOutboxRelayBackgroundService>(); // Sprint audit #AUDIT-25
    }

    private static void AddHelpers(this IServiceCollection services)
    {
        services.AddScoped<IPriorityCalculator, PriorityCalculator>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITicketCodeGenerator, TicketCodeGenerator>();
        services.AddScoped<IKbCodeGenerator, KbCodeGenerator>();
        services.AddScoped<ISlaCalculator, SlaCalculator>();
        services.AddScoped<ISlaService, SlaService>();

        // Override CurrentUserService from Shared
        services.AddScoped<TicketCurrentUserService>();
        services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());
        services.AddScoped<ITicketCurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());

        // Realtime Chat Services
        services.AddScoped<IChatAuthorizationService, ChatAuthorizationService>();
        services.AddScoped<ITicketChatRealtimeNotifier, SignalRTicketChatNotifier>();
        services.AddScoped<IMarkdownRenderer, MarkdigMarkdownRenderer>();

        // Chat Authorization + Validation (#518/#519)
        services.AddScoped<ISpamDetector, SpamDetector>();
        services.AddScoped<IProfanityFilter, ProfanityFilter>();
        services.AddScoped<IPiiDetector, PiiDetector>();  // PiiDetector giờ inject ICacheService (#559)

        // Group mention resolver (#537)
        services.AddScoped<IGroupMentionResolverService, LocalGroupMentionResolver>();

        // #557 — User connection tracker (Singleton vì in-memory, stateful across requests)
        services.AddSingleton<IUserConnectionTracker, InMemoryUserConnectionTracker>();

        // #552 — Chat cache service
        services.AddScoped<IChatCacheService, ChatCacheService>();

        // #547 — Template renderer
        services.AddScoped<ITemplateRenderer, TemplateRendererService>();

        // #564 — KB suggestion service
        services.AddScoped<IKbSuggestionService, KbSuggestionService>();

        // #568 — PDF exporter
        services.AddScoped<IPdfExporter, QuestPdfChatExporter>();

        // #559/#560 — Gemini implementations (luôn đăng ký, dùng khi Chat:Provider = "Gemini")
        services.AddHttpClient<GeminiChatAiClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.Ai.TimeoutSeconds));
        });
        services.AddHttpClient<GeminiChatTextAiClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.Ai.TimeoutSeconds));
        });

        // #559/#560 — DeepSeek implementations (luôn đăng ký, dùng khi Chat:Provider = "DeepSeek")
        services.AddHttpClient<DeepSeekChatAiClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.DeepSeek.TimeoutSeconds));
        });
        // DeepSeekChatTextAiClient tái sử dụng HttpClient của DeepSeekChatAiClient qua inner client
        services.AddTransient<DeepSeekChatTextAiClient>();

        // Factory chọn provider dựa theo Chat:Provider — đổi provider chỉ cần sửa appsettings
        services.AddScoped<IChatAiSuggestionClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            return opts.Provider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<DeepSeekChatAiClient>()
                : sp.GetRequiredService<GeminiChatAiClient>();
        });
        services.AddScoped<IChatTextAiClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            return opts.Provider.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<DeepSeekChatTextAiClient>()
                : sp.GetRequiredService<GeminiChatTextAiClient>();
        });

        // #514 — ClamAV REST client (typed HttpClient)
        services.AddHttpClient<IClamAvClient, ClamAvHttpClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.BaseAddress = new Uri(opts.VirusScan.Endpoint);
            http.Timeout = TimeSpan.FromSeconds(Math.Max(10, opts.VirusScan.TimeoutSeconds));
        });

        // #514 — Named HttpClient cho VirusScanWorker download file từ FileStorageService
        services.AddHttpClient("FileDownload", (sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.BaseAddress = new Uri(opts.VirusScan.FileStorageBaseUrl);
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        // #567 — Gemini voice transcription client (multimodal, timeout từ Chat:Voice:TranscribeTimeoutSeconds)
        services.AddHttpClient<IVoiceTranscriptionService, GeminiVoiceTranscriptionService>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ChatOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(Math.Max(15, opts.Voice.TranscribeTimeoutSeconds));
        });

        // #567 — FileStorageService upload client (dùng Bearer token forwarded từ original request)
        services.AddHttpClient<IFileUploadClient, FileStorageApiClient>((_, http) =>
        {
            http.Timeout = TimeSpan.FromSeconds(60);
        });
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
        services.AddScoped<TicketDataSeeder>();
        services.AddHttpContextAccessor();
    }
}
