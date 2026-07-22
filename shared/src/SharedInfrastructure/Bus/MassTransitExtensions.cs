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
                cfg.Host(configuration["RabbitMQ:Host"], "/", h =>
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

                // Hook per-service TRƯỚC ConfigureEndpoints — vd UseMessageScheduler cho saga timer.
                configureBus?.Invoke(context, cfg);

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IMessageProducerService, MassTransitProducer>();
        services.AddScoped<IIntegrationEventTransport, MassTransitProducer>();
        return services;
    }
}
