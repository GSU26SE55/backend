using System.Text.Json;
using FileStorageService.Domain.Enums;
using FileStorageService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Metrics;

namespace FileStorageService.Infrastructure.BackgroundJobs;

/// <summary>Relay audit pipeline FileStorageService (Sprint audit #AUDIT-29) — pattern giống AuthService #AUDIT-08. Redis leader election (D12).</summary>
public class FileAuditOutboxRelayBackgroundService : BackgroundService
{
    private const int PollIntervalSeconds = 2;
    private const int BatchSize = 50;
    private const int MaxRetries = 5;
    private const string LeaderKey = "file_audit_outbox_leader";
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<FileAuditOutboxRelayBackgroundService> _logger;

    public FileAuditOutboxRelayBackgroundService(IServiceScopeFactory scopeFactory, IDistributedCache cache,
        ILogger<FileAuditOutboxRelayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollIntervalSeconds));
        _logger.LogInformation("FileAuditOutboxRelay started (instance={Instance}).", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                if (await IsLeaderAsync(stoppingToken))
                    await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "FileAuditOutboxRelay tick failed."); }
        }
    }

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
            _logger.LogWarning(ex, "FileAuditOutboxRelay leader-election lỗi — fallback process.");
            return true;
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await db.FileAuditOutboxes
            .Where(o => o.Status == AuditOutboxStatusEnum.Pending && o.RetryCount < MaxRetries)
            .OrderBy(o => o.CreatedAt).Take(BatchSize).ToListAsync(ct);

        // #AUDIT-44 — gauge pending count.
        var totalPending = await db.FileAuditOutboxes.CountAsync(o => o.Status == AuditOutboxStatusEnum.Pending, ct);
        AppMetrics.AuditOutboxPending.WithLabels("FileStorageService").Set(totalPending);

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
                _logger.LogWarning(ex, "FileAuditOutboxRelay publish fail {Id} (retry {Retry}).", msg.Id, msg.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
