using System.Text.Json;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Metrics;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// Relay RIÊNG cho audit pipeline (Sprint audit #AUDIT-08) — KHÔNG dùng chung <see cref="OutboxRelayBackgroundService"/>
/// (AUTH-15) vì schema khác (audit_outbox + status enum).
///
/// <para>Poll <c>audit_outbox</c> mỗi 2s, batch 50, publish <see cref="AuditCreatedEventV1"/> qua MassTransit,
/// mark <see cref="AuditOutboxStatusEnum.Published"/>. Honor <c>CancellationToken</c>.</para>
///
/// <para><b>Single-instance enforce qua Redis leader election (D12):</b> mỗi tick acquire/renew lease key
/// <c>audit_outbox_leader</c> (TTL 30s) qua <see cref="IDistributedCache"/>; chỉ leader process. Best-effort —
/// idempotent consumer (unique event_id, #AUDIT-15) là last-line defense cho race window.</para>
/// </summary>
public class AuditOutboxRelayBackgroundService : BackgroundService
{
    private const int PollIntervalSeconds = 2;
    private const int BatchSize = 50;
    private const int MaxRetries = 5;
    private const string LeaderKey = "audit_outbox_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AuditOutboxRelayBackgroundService> _logger;

    public AuditOutboxRelayBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        ILogger<AuditOutboxRelayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollIntervalSeconds));
        _logger.LogInformation("AuditOutboxRelay started (instance={Instance}).", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) { break; }

            try
            {
                if (await IsLeaderAsync(stoppingToken))
                    await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditOutboxRelay tick failed.");
            }
        }

        _logger.LogInformation("AuditOutboxRelay shutting down.");
    }

    /// <summary>Best-effort Redis leader election: acquire nếu trống hoặc đã sở hữu, renew TTL.</summary>
    private async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        try
        {
            var current = await _cache.GetStringAsync(LeaderKey, ct);
            if (current is null || current == _instanceId)
            {
                await _cache.SetStringAsync(LeaderKey, _instanceId,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LeaseTtl }, ct);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Redis lỗi → fallback chạy (idempotent consumer bảo vệ duplicate).
            _logger.LogWarning(ex, "AuditOutboxRelay leader-election lỗi — fallback process (idempotent backstop).");
            return true;
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await db.AuditOutboxes
            .Where(o => o.Status == AuditOutboxStatusEnum.Pending && o.RetryCount < MaxRetries)
            .OrderBy(o => o.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        // #AUDIT-44 — gauge pending count.
        var totalPending = await db.AuditOutboxes.CountAsync(o => o.Status == AuditOutboxStatusEnum.Pending, ct);
        AppMetrics.AuditOutboxPending.WithLabels("AuthService").Set(totalPending);

        if (pending.Count == 0)
            return;

        foreach (var msg in pending)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                var evt = JsonSerializer.Deserialize<AuditCreatedEventV1>(msg.Payload);
                if (evt is null)
                {
                    msg.RetryCount += 1;
                    msg.LastError = "Deserialize AuditCreatedEventV1 returned null.";
                    msg.Status = msg.RetryCount >= MaxRetries ? AuditOutboxStatusEnum.Failed : AuditOutboxStatusEnum.Pending;
                    continue;
                }

                await publish.Publish(evt, ct);
                msg.Status = AuditOutboxStatusEnum.Published;
                msg.ProcessedAt = DateTime.UtcNow;
                msg.LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                msg.RetryCount += 1;
                msg.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (msg.RetryCount >= MaxRetries)
                    msg.Status = AuditOutboxStatusEnum.Failed;
                _logger.LogWarning(ex, "AuditOutboxRelay publish fail {Id} (retry {Retry}/{Max}).",
                    msg.Id, msg.RetryCount, MaxRetries);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
