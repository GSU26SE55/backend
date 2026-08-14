using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Metrics;
using SmsService.Application.Interfaces.Services;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.BackgroundJobs;

/// <summary>
/// Poll bảng <c>outbox_messages</c> mỗi <c>PollIntervalSeconds</c>, publish lên RabbitMQ và mark <c>ProcessedAt</c>.
/// RabbitMQ down → publish throw → tăng <c>RetryCount</c>, giữ <c>ProcessedAt = null</c> để tick sau retry.
/// Cap <c>MaxRetries</c> tránh poison message lặp vô tận.
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
    /// <summary>
    /// GH-794 — thời hạn giữ một dòng outbox. Phải dài hơn hẳn một lần publish chậm nhất: ngắn quá
    /// là quyền hết hạn khi lần publish vẫn đang chạy, và replica khác gửi lại chính tin nhắn đó
    /// (với SMS thì mỗi lần trùng là một tin tính phí).
    /// </summary>
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>Định danh instance — dùng làm chủ sở hữu của quyền giữ dòng.</summary>
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

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
        var claims = scope.ServiceProvider.GetRequiredService<IOutboxClaimService>();

        // GH-794 — chỉ lấy những dòng thật sự nhận được: chưa xử lý VÀ chưa ai giữ (hoặc quyền đã
        // hết hạn). Đây mới là lọc sơ bộ; ai được dòng nào do câu UPDATE ở TryClaimAsync quyết.
        var claimCutoff = DateTime.UtcNow;
        var pending = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(o => o.ProcessedAt == null
                        && o.RetryCount < _options.MaxRetries
                        && (o.LeaseUntilUtc == null || o.LeaseUntilUtc <= claimCutoff))
            .OrderBy(o => o.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var totalPending = await dbContext.OutboxMessages.CountAsync(o => o.ProcessedAt == null, cancellationToken);
        AppMetrics.OutboxPending.Set(totalPending);

        if (pending.Count == 0)
            return;

        foreach (var msg in pending)
        {
            // GH-794 — GIÀNH dòng trước khi làm bất cứ điều gì. Không giành được nghĩa là replica
            // khác đang publish chính dòng này; bỏ qua, không phải lỗi.
            var claimed = await claims.TryClaimAsync(msg.Id, _instanceId, ClaimLease, cancellationToken);
            if (claimed is null)
                continue;

            try
            {
                var eventType = Type.GetType(claimed.EventType);
                if (eventType is null)
                {
                    var reason = $"Cannot resolve type '{claimed.EventType}'.";
                    await claims.MarkFailedAsync(msg.Id, _instanceId, reason, cancellationToken);
                    _logger.LogError("[SmsService] OutboxRelay cannot resolve type {Type} for message {Id}.", claimed.EventType, msg.Id);
                    AppMetrics.OutboxFailures.WithLabels("type_not_found").Inc();
                    if (claimed.RetryCount + 1 >= _options.MaxRetries)
                        AppMetrics.OutboxSkippedMaxRetry.Inc();
                    continue;
                }

                var eventObj = JsonSerializer.Deserialize(claimed.Payload, eventType);
                if (eventObj is null)
                {
                    var reason = $"Deserialize returned null for type '{claimed.EventType}'.";
                    await claims.MarkFailedAsync(msg.Id, _instanceId, reason, cancellationToken);
                    AppMetrics.OutboxFailures.WithLabels("deserialize_null").Inc();
                    if (claimed.RetryCount + 1 >= _options.MaxRetries)
                        AppMetrics.OutboxSkippedMaxRetry.Inc();
                    continue;
                }

                await publishEndpoint.Publish(eventObj, eventType, cancellationToken);

                // Đánh dấu NGAY sau khi publish, không gom tới cuối lô: tiến trình chết giữa lô mà
                // mới ghi ở cuối thì mọi tin đã gửi trong lô đó sẽ được gửi lại từ đầu.
                await claims.MarkProcessedAsync(msg.Id, _instanceId, cancellationToken);

                AppMetrics.OutboxProcessed.WithLabels(eventType.Name).Inc();
            }
            catch (Exception ex)
            {
                var reason = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await claims.MarkFailedAsync(msg.Id, _instanceId, reason, cancellationToken);
                _logger.LogWarning(ex, "[SmsService] OutboxRelay failed to publish message {Id} (retry {Retry}/{Max}).",
                    msg.Id, claimed.RetryCount + 1, _options.MaxRetries);

                AppMetrics.OutboxFailures.WithLabels(ex.GetType().Name).Inc();
                if (claimed.RetryCount + 1 >= _options.MaxRetries)
                    AppMetrics.OutboxSkippedMaxRetry.Inc();
            }
        }
    }
}
