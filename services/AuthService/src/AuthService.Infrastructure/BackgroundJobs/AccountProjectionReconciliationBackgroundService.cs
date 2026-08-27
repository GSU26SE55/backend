using AuthService.Application.CQRS.Command.Account;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// Republishes a complete, authoritative account snapshot on a fixed interval.
/// Realtime lifecycle events keep projections fresh; this worker is the repair loop that
/// guarantees eventual convergence after downtime, poison-message recovery, or manual drift.
/// </summary>
public sealed class AccountProjectionReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountProjectionReconciliationOptions _options;
    private readonly ILogger<AccountProjectionReconciliationBackgroundService> _logger;

    public AccountProjectionReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AccountProjectionReconciliationOptions> options,
        ILogger<AccountProjectionReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Account projection reconciliation is disabled.");
            return;
        }

        _logger.LogInformation(
            "Account projection reconciliation started. Interval={IntervalMinutes} minute(s).",
            _options.IntervalMinutes);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));
        do
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Account projection reconciliation tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new AccountResyncCommand(), cancellationToken);

        _logger.LogInformation(
            "Account projection reconciliation published {PublishedCount} authoritative snapshot(s).",
            result.Data?.TotalAccounts ?? 0);
    }
}
