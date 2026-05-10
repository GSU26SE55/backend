using System.Text.Json;
using AuthService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Metrics;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// Background job đọc bảng outbox_messages mỗi N giây, publish lên RabbitMQ và mark ProcessedAt.
/// Khi RabbitMQ down: publish throw → tăng RetryCount, giữ nguyên ProcessedAt=null. Lần tick sau retry.
/// Cap MaxRetries để tránh poison message lặp vô tận.
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
        _logger.LogInformation("OutboxRelay started. Interval={Seconds}s, BatchSize={Batch}, MaxRetries={Max}",
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
                _logger.LogError(ex, "OutboxRelay tick failed unexpectedly.");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await dbContext.OutboxMessages
            .Where(o => o.ProcessedAt == null && o.RetryCount < _options.MaxRetries)
            .OrderBy(o => o.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        // Update gauge: tổng số message còn pending (kể cả message đã quá MaxRetries — poison) để Prometheus thấy backlog thực sự.
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
                    _logger.LogError("OutboxRelay cannot resolve type {Type} for message {Id}.", msg.EventType, msg.Id);
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

                // Label = short event-type name (vd "UserRegisteredEvent") để Prometheus aggregate by event.
                AppMetrics.OutboxProcessed.WithLabels(eventType.Name).Inc();
            }
            catch (Exception ex)
            {
                msg.RetryCount += 1;
                msg.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                _logger.LogWarning(ex, "OutboxRelay failed to publish message {Id} (retry {Retry}/{Max}).",
                    msg.Id, msg.RetryCount, _options.MaxRetries);

                // Reason = exception type (vd "BrokerUnreachableException") để dashboard breakdown nguyên nhân fail.
                AppMetrics.OutboxFailures.WithLabels(ex.GetType().Name).Inc();
                if (msg.RetryCount >= _options.MaxRetries)
                    AppMetrics.OutboxSkippedMaxRetry.Inc();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
