using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.BackgroundJobs;

/// <summary>
/// TTL redactor — xóa cột <c>message</c> 24h sau khi SMS đạt trạng thái <c>Sent</c>.
/// Cần cho Android render plaintext trong vòng đời, nhưng giảm phơi nhiễm dài hạn.
/// Tick 15 phút, batch 500.
/// </summary>
public class SmsMessageRedactorBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetainAfterSent = TimeSpan.FromHours(24);
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsMessageRedactorBackgroundService> _logger;

    public SmsMessageRedactorBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SmsMessageRedactorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        _logger.LogInformation("[SmsService] SmsMessageRedactor started. Tick={Tick}, RetainAfterSent={Retain}",
            TickInterval, RetainAfterSent);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
                var now = DateTime.UtcNow;
                var cutoff = now - RetainAfterSent;

                var candidates = await db.SmsMessages
                    .Where(x => x.Status == SmsStatus.Sent
                                && x.SentAt != null
                                && x.SentAt < cutoff
                                && x.Message != null
                                && !x.IsDeleted)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                if (candidates.Count == 0)
                    continue;

                foreach (var m in candidates)
                {
                    m.Redact(now);
                    db.SmsAuditLogs.Add(new SmsAuditLog
                    {
                        Id = Guid.NewGuid(),
                        SmsMessageId = m.Id,
                        Event = SmsAuditEvent.Redacted,
                        CreatedAt = now,
                        Detail = "Message content redacted after 24h retention."
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("[SmsService] Redacted {Count} SMS messages older than 24h.", candidates.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SmsService] SmsMessageRedactor tick failed.");
            }
        }
    }
}
