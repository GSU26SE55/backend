using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Metrics;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.BackgroundJobs;

/// <summary>
/// Poll bảng <c>outbox_messages</c> mỗi <c>PollIntervalSeconds</c>, publish lên RabbitMQ và mark <c>ProcessedAt</c>.
/// RabbitMQ down → publish throw → tăng <c>RetryCount</c>, giữ <c>ProcessedAt = null</c> để tick sau retry.
/// Cap <c>MaxRetries</c> tránh poison message lặp vô tận.
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelayBackgroundService> _logger;

    public OutboxRelayBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxRelayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        _logger.LogInformation("[SmsService] OutboxRelay started. Interval={Seconds}s, BatchSize={Batch}, MaxRetries={Max}",
            _options.PollIntervalSeconds, _options.BatchSize, _options.MaxRetries);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SmsService] OutboxRelay tick failed unexpectedly.");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await dbContext.OutboxMessages
            .Where(o => o.ProcessedAt == null && o.RetryCount < _options.MaxRetries)
            .OrderBy(o => o.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var totalPending = await dbContext.OutboxMessages.CountAsync(o => o.ProcessedAt == null, cancellationToken);
        AppMetrics.OutboxPending.Set(totalPending);

        if (pending.Count == 0)
            return;

        foreach (var msg in pending)
        {
            try
            {
                var eventType = Type.GetType(msg.EventType);
                if (eventType is null)
                {
                    msg.RetryCount += 1;
                    msg.LastError = $"Cannot resolve type '{msg.EventType}'.";
                    _logger.LogError("[SmsService] OutboxRelay cannot resolve type {Type} for message {Id}.", msg.EventType, msg.Id);
                    AppMetrics.OutboxFailures.WithLabels("type_not_found").Inc();
                    if (msg.RetryCount >= _options.MaxRetries)
                        AppMetrics.OutboxSkippedMaxRetry.Inc();
                    continue;
                }

                var eventObj = JsonSerializer.Deserialize(msg.Payload, eventType);
                if (eventObj is null)
                {
                    msg.RetryCount += 1;
                    msg.LastError = $"Deserialize returned null for type '{msg.EventType}'.";
                    AppMetrics.OutboxFailures.WithLabels("deserialize_null").Inc();
                    if (msg.RetryCount >= _options.MaxRetries)
                        AppMetrics.OutboxSkippedMaxRetry.Inc();
                    continue;
                }

                await publishEndpoint.Publish(eventObj, eventType, cancellationToken);
                msg.ProcessedAt = DateTime.UtcNow;
                msg.LastError = null;

                AppMetrics.OutboxProcessed.WithLabels(eventType.Name).Inc();
            }
            catch (Exception ex)
            {
                msg.RetryCount += 1;
                msg.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                _logger.LogWarning(ex, "[SmsService] OutboxRelay failed to publish message {Id} (retry {Retry}/{Max}).",
                    msg.Id, msg.RetryCount, _options.MaxRetries);

                AppMetrics.OutboxFailures.WithLabels(ex.GetType().Name).Inc();
                if (msg.RetryCount >= _options.MaxRetries)
                    AppMetrics.OutboxSkippedMaxRetry.Inc();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
