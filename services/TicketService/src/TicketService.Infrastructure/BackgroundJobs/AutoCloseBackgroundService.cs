using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

public class AutoCloseBackgroundService : BackgroundService
{
    private readonly ILogger<AutoCloseBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public AutoCloseBackgroundService(ILogger<AutoCloseBackgroundService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoCloseBackgroundService is starting.");

        stoppingToken.Register(() => _logger.LogInformation("AutoCloseBackgroundService is stopping."));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("AutoCloseBackgroundService is doing background work.");

            await CloseOldTickets(stoppingToken);

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }

        _logger.LogInformation("AutoCloseBackgroundService has stopped.");
    }

    private async Task CloseOldTickets(CancellationToken stoppingToken)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AutoCloseBackgroundService>>();

            try
            {
                var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
                var ticketsToClose = await unitOfWork.Tickets.FindAsync(
                    t => t.Status == TicketStatusEnum.ClosedPendingRate && t.ApprovedAt < sevenDaysAgo
                ).ToListAsync(stoppingToken);

                if (!ticketsToClose.Any())
                {
                    logger.LogInformation("No tickets to auto-close.");
                    return;
                }

                logger.LogInformation($"Found {ticketsToClose.Count} tickets to auto-close.");

                foreach (var ticket in ticketsToClose)
                {
                    ticket.Status = TicketStatusEnum.Closed;
                    ticket.ClosedAt = DateTime.UtcNow;

                    var activity = new TicketActivity
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticket.Id,
                        Action = ActivityActionEnum.AutoClosed,
                        ActorRole = ActorRoleEnum.System,
                        ActorDisplayName = "System",
                        Reason = "Ticket automatically closed after 7 days in 'ClosedPendingRate' status.",
                        Ticket = ticket
                    };

                    await unitOfWork.TicketActivities.AddAsync(activity);
                }

                await unitOfWork.CommitTransactionAsync();
                logger.LogInformation("Successfully auto-closed {Count} tickets.", ticketsToClose.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while auto-closing tickets.");
                await unitOfWork.RollbackTransactionAsync();
            }
        }
    }
}
