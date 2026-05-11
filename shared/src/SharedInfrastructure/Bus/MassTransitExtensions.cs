using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Bus;

public static class MassTransitExtensions
{
    /// <summary>
    /// Wire MassTransit (RabbitMQ) + Correlation ID filters + Resilience policy.
    ///
    /// Resilience layers (theo thứ tự áp dụng cho message fail):
    /// 1. <c>UseMessageRetry</c> — Exponential backoff trong cùng consumer process.
    ///    Phù hợp transient ngắn (network blip, DB lock contention). Default: 5 attempts, 1s → 30s.
    /// 2. <c>UseScheduledRedelivery</c> — Re-enqueue sau N phút khi step 1 exhausted.
    ///    Phù hợp outage dài (Mailjet down, RabbitMQ disconnect). Default: 5, 15, 30 phút.
    /// 3. Sau khi cả 2 cạn → MassTransit tự move sang <c>{queue}_error</c> (DLQ pattern).
    ///
    /// Exceptions KHÔNG được retry (fail fast vào DLQ):
    /// - <see cref="ArgumentException"/> / <see cref="ArgumentNullException"/>: bug logic, retry vô ích.
    /// - <see cref="System.ComponentModel.DataAnnotations.ValidationException"/>: payload sai format.
    /// - <see cref="UnauthorizedAccessException"/>: permission issue, không tự sửa được.
    /// - <see cref="NotImplementedException"/>: rõ ràng bug.
    /// - <c>BusinessException</c> custom (nếu service định nghĩa): handler-level decision.
    /// </summary>
    public static IServiceCollection AddMessageBus(this IServiceCollection services, IConfiguration configuration, params System.Reflection.Assembly[] consumerAssemblies)
    {
        services.AddHttpContextAccessor();

        // Bind resilience options 1 lần để tránh re-read mỗi lần startup.
        var resilience = new ResilienceOptions();
        configuration.GetSection(ResilienceOptions.SectionName).Bind(resilience);
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));

        services.AddMassTransit(x =>
        {
            if (consumerAssemblies != null && consumerAssemblies.Length > 0)
            {
                x.AddConsumers(consumerAssemblies);
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(configuration["RabbitMQ:Username"]!);
                    h.Password(configuration["RabbitMQ:Password"]!);
                });

                // ============== Resilience layer 1: in-process retry ==============
                // Áp dụng GLOBALLY cho mọi consumer endpoint.
                // Exponential backoff: lần 1 sau 1s, sau đó tăng theo intervalDelta, capped at maxInterval.
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(
                        retryLimit: resilience.MessageRetry.Limit,
                        minInterval: TimeSpan.FromSeconds(resilience.MessageRetry.MinIntervalSeconds),
                        maxInterval: TimeSpan.FromSeconds(resilience.MessageRetry.MaxIntervalSeconds),
                        intervalDelta: TimeSpan.FromSeconds(resilience.MessageRetry.IntervalDeltaSeconds));

                    // KHÔNG retry các exception là "bug" hoặc "business invalid" — fail-fast vào DLQ.
                    r.Ignore<ArgumentException>();
                    r.Ignore<ArgumentNullException>();
                    r.Ignore<System.ComponentModel.DataAnnotations.ValidationException>();
                    r.Ignore<UnauthorizedAccessException>();
                    r.Ignore<NotImplementedException>();
                });

                // ============== Resilience layer 2: scheduled redelivery (long outage) ==============
                if (resilience.ScheduledRedelivery.Enabled)
                {
                    var intervals = resilience.ScheduledRedelivery
                        .GetIntervalsMinutes()
                        .Select(m => TimeSpan.FromMinutes(m))
                        .ToArray();

                    if (intervals.Length > 0)
                    {
                        // RabbitMQ delayed-exchange plugin yêu cầu BEFORE ConfigureEndpoints.
                        // Trong MassTransit 8.x dùng UseDelayedMessageScheduler để bus tự enable delayed exchange.
                        cfg.UseDelayedMessageScheduler();
                        cfg.UseScheduledRedelivery(r => r.Intervals(intervals));
                    }
                }

                // Forward Correlation ID qua message header khi publish, đọc lại khi consume.
                cfg.UsePublishFilter(typeof(CorrelationIdPublishFilter<>), context);
                cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IMessageProducerService, MassTransitProducer>();
        return services;
    }
}
