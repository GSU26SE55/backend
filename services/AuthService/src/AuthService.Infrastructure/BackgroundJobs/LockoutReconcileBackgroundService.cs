using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// #AUTH-18 + #AUTH-43: dọn account đang Locked có <c>LockoutEndAt</c> qua → set Status=Active +
/// LockoutEndAt=null + FailedLoginAttempts=0. Trước khi có job này, account chỉ tự unlock khi user thử
/// login lại (handler check). Nếu user không login lại → status mãi mãi Locked, dù lockout đã hết.
///
/// Tần suất: mỗi 5 phút.
/// </summary>
public class LockoutReconcileBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LockoutReconcileBackgroundService> _logger;

    public LockoutReconcileBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LockoutReconcileBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LockoutReconcile started. Poll={Minutes}min.", PollInterval.TotalMinutes);
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LockoutReconcile tick failed; will retry next interval.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("LockoutReconcile stopped.");
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageProducer = scope.ServiceProvider.GetRequiredService<IMessageProducerService>();

        var nowUtc = DateTime.UtcNow;
        var affected = 0;

        while (!ct.IsCancellationRequested)
        {
            var expired = await db.Users
                .Include(account => account.Role)
                .Where(account => account.Status == AccountStatusEnum.Locked
                                  && account.LockoutEndAt.HasValue
                                  && account.LockoutEndAt.Value <= nowUtc
                                  && !account.IsDeleted)
                .OrderBy(account => account.LockoutEndAt)
                .ThenBy(account => account.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (expired.Count == 0)
                break;

            foreach (var account in expired)
            {
                account.Status = AccountStatusEnum.Active;
                account.LockoutEndAt = null;
                account.FailedLoginAttempts = 0;

                await messageProducer.PublishAsync(new AccountStatusChangedEvent(
                    account.Id,
                    account.Email,
                    (int)AccountStatusEnum.Locked,
                    (int)AccountStatusEnum.Active,
                    "Lockout expired — scheduled auto-unlock.",
                    Role: account.Role?.Name ?? string.Empty,
                    FullName: account.FullName,
                    PhoneNumber: account.PhoneNumber,
                    IsActive: true), ct);
            }

            await db.SaveChangesAsync(ct);
            affected += expired.Count;

            if (expired.Count < BatchSize)
                break;
        }

        if (affected > 0)
            _logger.LogInformation("LockoutReconcile auto-unlocked {Count} account(s).", affected);
    }
}
