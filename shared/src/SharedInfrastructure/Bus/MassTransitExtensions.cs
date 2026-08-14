using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Bus;

public static class MassTransitExtensions
{
    /// <summary>
    /// Đăng ký MassTransit + RabbitMQ + Correlation filters.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Config root (đọc <c>RabbitMQ:Host/Username/Password</c>).</param>
    /// <param name="consumerAssemblies">Assemblies chứa <c>IConsumer&lt;T&gt;</c> implementations.</param>
    public static IServiceCollection AddMessageBus(this IServiceCollection services, IConfiguration configuration, params System.Reflection.Assembly[] consumerAssemblies)
        => services.AddMessageBus(configuration, configure: null, consumerAssemblies);

    /// <summary>
    /// Đăng ký MassTransit + RabbitMQ + Correlation filters, kèm hook tùy biến
    /// (vd add Saga state machine, EF Outbox, Quartz scheduler — xem Sprint 5B #235/#237).
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Config root.</param>
    /// <param name="configure">Action tùy biến chạy bên trong <c>AddMassTransit</c> trước khi <c>UsingRabbitMq</c>.</param>
    /// <param name="consumerAssemblies">Assemblies chứa consumers.</param>
    public static IServiceCollection AddMessageBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure,
        params System.Reflection.Assembly[] consumerAssemblies)
        => services.AddMessageBus(configuration, configure, configureBus: null, consumerAssemblies);

    /// <summary>
    /// Như overload trên, thêm hook <paramref name="configureBus"/> chạy BÊN TRONG
    /// <c>UsingRabbitMq</c> — dùng khi service cần cấu hình pipeline bus, vd
    /// <c>cfg.UseMessageScheduler(...)</c> cho saga có timer (TicketService).
    /// </summary>
    /// <param name="configureBus">
    /// Chạy sau host/filter, TRƯỚC <c>ConfigureEndpoints</c>. Null = giữ hành vi mặc định.
    /// </param>
    public static IServiceCollection AddMessageBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus,
        params System.Reflection.Assembly[] consumerAssemblies)
        => services.AddMessageBus(
            configuration, configure, configureBus, excludedConsumerTypes: null, consumerAssemblies);

    /// <summary>
    /// Như overload trên, thêm <paramref name="excludedConsumerTypes"/> để LOẠI TRỪ consumer
    /// khỏi assembly scan — dùng khi 1 consumer bị thay thế bởi Saga và không được chạy song song
    /// (vd <c>BatteryAnomalyDetectedConsumer</c> khi <c>AlertTicketSagaEnabled=true</c>).
    /// </summary>
    /// <param name="excludedConsumerTypes">Consumer types bỏ qua khi scan. Null/rỗng = scan hết.</param>
    public static IServiceCollection AddMessageBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus,
        IEnumerable<Type>? excludedConsumerTypes,
        params System.Reflection.Assembly[] consumerAssemblies)
    {
        services.AddHttpContextAccessor();

        services.AddMassTransit(x =>
        {
            if (consumerAssemblies != null && consumerAssemblies.Length > 0)
            {
                var excluded = excludedConsumerTypes?.ToHashSet() ?? new HashSet<Type>();
                if (excluded.Count > 0)
                    x.AddConsumers(type => !excluded.Contains(type), consumerAssemblies);
                else
                    x.AddConsumers(consumerAssemblies);
            }

            // Hook tùy biến — add Saga, Quartz, EF Outbox, … (per-service).
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var virtualHost = configuration["RabbitMQ:VirtualHost"];
                if (string.IsNullOrWhiteSpace(virtualHost))
                    virtualHost = "/";

                cfg.Host(configuration["RabbitMQ:Host"], virtualHost, h =>
                {
                    h.Username(configuration["RabbitMQ:Username"]!);
                    h.Password(configuration["RabbitMQ:Password"]!);
                });

                // Forward Correlation ID qua message header khi publish, đọc lại khi consume.
                cfg.UsePublishFilter(typeof(CorrelationIdPublishFilter<>), context);
                cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);

                // Sprint 5B #235 — runtime config endpoint default
                // (per-endpoint override qua ReceiveEndpoint nếu cần).
                cfg.PrefetchCount = configuration.GetValue<ushort?>("MessageBus:PrefetchCount") ?? 16;
                cfg.ConcurrentMessageLimit = configuration.GetValue<int?>("MessageBus:ConcurrentMessageLimit") ?? 8;

                // ── Sprint 6.3 NOTI3-08 (#708) — retry + delayed redelivery ──────────────
                //
                // Trước sprint này KHÔNG có UseMessageRetry lẫn UseDelayedRedelivery: consumer ném
                // exception đúng 1 lần là message rơi thẳng vào queue `_error` và không ai theo dõi.
                // Một trục trặc thoáng qua (Redis chớp, DB timeout, provider 503) cũng đủ mất message.
                //
                // Hai tầng, khác nhau về mục đích:
                //   1. UseMessageRetry      — retry NGAY trong tiến trình, cho lỗi thoáng qua (ms→giây).
                //                             KHÔNG phụ thuộc hạ tầng ⇒ BẬT MẶC ĐỊNH.
                //   2. UseDelayedRedelivery — trả message về broker rồi giao lại sau nhiều phút, cho
                //                             lỗi kéo dài (provider sập).
                //
                // ⚠️ Redelivery MẶC ĐỊNH TẮT. Trên RabbitMQ nó cần plugin cộng đồng
                // `rabbitmq_delayed_message_exchange` (kiểu exchange `x-delayed-message`).
                // Image đang dùng — `rabbitmq:3-management-alpine` — CHỈ bật rabbitmq_prometheus và
                // rabbitmq_management (xem docker-compose.yml), KHÔNG có plugin này. Bật lên mà thiếu
                // plugin thì việc khai báo exchange thất bại NGAY LÚC BUS KHỞI ĐỘNG ⇒ chết TẤT CẢ service dùng bus.
                //
                // Muốn dùng: cài plugin (.ez) vào image rồi đặt MessageBus:Redelivery:Enabled=true.
                //
                // ⚠️ Retry an toàn được là nhờ NOTI3-09 (#709) làm dedup atomic TRƯỚC. Bật retry trên
                // consumer chưa idempotent thật sự sẽ NHÂN BẢN notification chứ không phải gửi lại
                // (rủi ro R-41). Consumer của NotificationService chặn trùng bằng SET NX (debounce)
                // hoặc IInboxStore, nên message giao lại được nhận diện và bỏ qua.
                var retryLimit = configuration.GetValue<int?>("MessageBus:Retry:Limit") ?? 3;
                var retryInitialMs = configuration.GetValue<int?>("MessageBus:Retry:InitialIntervalMs") ?? 200;
                var retryMaxMs = configuration.GetValue<int?>("MessageBus:Retry:MaxIntervalMs") ?? 5000;

                var redeliveryEnabled = configuration.GetValue<bool?>("MessageBus:Redelivery:Enabled") ?? false;
                var redeliveryMinutes = configuration.GetSection("MessageBus:Redelivery:IntervalsMinutes")
                                            .Get<int[]>() ?? new[] { 5, 15, 60 };

                // Thứ tự khai báo quan trọng: redelivery bọc NGOÀI retry — chuỗi retry ngắn chạy hết
                // rồi mới tính tới lần giao lại có độ trễ.
                if (redeliveryEnabled && redeliveryMinutes.Length > 0)
                {
                    cfg.UseDelayedRedelivery(r => r.Intervals(
                        redeliveryMinutes.Select(m => TimeSpan.FromMinutes(m)).ToArray()));
                }

                if (retryLimit > 0)
                {
                    cfg.UseMessageRetry(r => r.Exponential(
                        retryLimit,
                        TimeSpan.FromMilliseconds(retryInitialMs),
                        TimeSpan.FromMilliseconds(retryMaxMs),
                        TimeSpan.FromMilliseconds(retryInitialMs)));
                }

                // Hook per-service TRƯỚC ConfigureEndpoints — vd publish scheduler cho saga timer.
                configureBus?.Invoke(context, cfg);

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IMessageProducerService, MassTransitProducer>();
        services.AddScoped<IIntegrationEventTransport, MassTransitProducer>();
        return services;
    }
}
