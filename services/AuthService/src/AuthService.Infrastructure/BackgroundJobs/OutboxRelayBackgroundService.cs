using System.Text.Json;
using AuthService.Application.Interfaces.Services;
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
///
/// #AUTH-37: honor CancellationToken graceful shutdown — mỗi async call pass token, loop check
/// IsCancellationRequested. GH-794: mỗi message được đánh dấu ngay sau khi publish nên shutdown
/// giữa lô không làm mất trạng thái; không còn cần flush cuối lô.
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
    /// <summary>
    /// GH-794 — thời hạn giữ một dòng outbox. Phải dài hơn hẳn một lần publish chậm nhất: ngắn quá
    /// là quyền hết hạn khi lần publish vẫn đang chạy, và replica khác publish lại chính dòng đó.
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
        _logger.LogInformation("OutboxRelay started. Interval={Seconds}s, BatchSize={Batch}, MaxRetries={Max}",
            _options.PollIntervalSeconds, _options.BatchSize, _options.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

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

        _logger.LogInformation("OutboxRelay shutting down gracefully.");
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var claims = scope.ServiceProvider.GetRequiredService<IOutboxClaimService>();

        // GH-794 — chỉ lấy những dòng thật sự nhận được: chưa xử lý VÀ chưa ai giữ (hoặc quyền đã
        // hết hạn). Đây mới chỉ là lọc sơ bộ; ai được dòng nào do câu UPDATE ở TryClaimAsync quyết.
        var now = DateTime.UtcNow;
        var pending = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(o => o.ProcessedAt == null
                        && o.RetryCount < _options.MaxRetries
                        && (o.LeaseUntilUtc == null || o.LeaseUntilUtc <= now))
            .OrderBy(o => o.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        // Update gauge: tổng số message còn pending (kể cả message đã quá MaxRetries — poison) để Prometheus thấy backlog thực sự.
        var totalPending = await dbContext.OutboxMessages.CountAsync(o => o.ProcessedAt == null, cancellationToken);
        AppMetrics.OutboxPending.Set(totalPending);

        if (pending.Count == 0)
            return;

        var stopRequested = false;
        foreach (var msg in pending)
        {
            // #AUTH-37: check cancellation BEFORE mỗi message để không publish thêm sau khi shutdown.
            if (cancellationToken.IsCancellationRequested)
            {
                stopRequested = true;
                break;
            }

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
                    _logger.LogError("OutboxRelay cannot resolve type {Type} for message {Id}.", claimed.EventType, msg.Id);
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
                // mới ghi ở cuối thì mọi message đã gửi trong lô đó sẽ được gửi lại từ đầu.
                await claims.MarkProcessedAsync(msg.Id, _instanceId, cancellationToken);

                // Label = short event-type name (vd "UserRegisteredEvent") để Prometheus aggregate by event.
                AppMetrics.OutboxProcessed.WithLabels(eventType.Name).Inc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // #AUTH-37: shutdown signal giữa lúc publish → stop loop, flush các message đã thành công.
                stopRequested = true;
                break;
            }
            catch (Exception ex)
            {
                var reason = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await claims.MarkFailedAsync(msg.Id, _instanceId, reason, cancellationToken);
                _logger.LogWarning(ex, "OutboxRelay failed to publish message {Id} (retry {Retry}/{Max}).",
                    msg.Id, claimed.RetryCount + 1, _options.MaxRetries);

                // Reason = exception type (vd "BrokerUnreachableException") để dashboard breakdown nguyên nhân fail.
                AppMetrics.OutboxFailures.WithLabels(ex.GetType().Name).Inc();
                if (claimed.RetryCount + 1 >= _options.MaxRetries)
                    AppMetrics.OutboxSkippedMaxRetry.Inc();
            }
        }

        // GH-794 — không còn flush cuối lô: mỗi message được đánh dấu ngay sau khi publish, nên
        // shutdown giữa chừng không làm mất trạng thái của phần đã gửi. Cờ stopRequested chỉ còn
        // nhiệm vụ ghi log cho rõ vì sao lô dừng sớm.
        if (stopRequested)
            _logger.LogInformation("OutboxRelay stopped mid-batch on shutdown; processed rows already persisted.");
    }
}
