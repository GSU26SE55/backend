using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.BackgroundJobs;

public sealed class TicketScheduleActivationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<TicketScheduleOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TicketScheduleActivationBackgroundService> _logger;

    public TicketScheduleActivationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<TicketScheduleOptions> options,
        ILogger<TicketScheduleActivationBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ActivateDueTicketsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ticket schedule activation tick failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds), stoppingToken);
        }
    }

    public async Task ActivateDueTicketsAsync(CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        List<(Guid TicketId, int ScheduleVersion)> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            due = await db.Tickets.AsNoTracking()
                .Where(ticket => !ticket.IsDeleted
                    && ticket.Status == TicketStatusEnum.Pending
                    && ticket.ScheduledStartAtUtc != null
                    && ticket.ScheduledStartAtUtc <= nowUtc)
                .OrderBy(ticket => ticket.ScheduledStartAtUtc)
                .ThenBy(ticket => ticket.Id)
                .Take(_options.Value.BatchSize)
                .Select(ticket => new ValueTuple<Guid, int>(ticket.Id, ticket.ScheduleVersion))
                .ToListAsync(ct);
        }

        foreach (var item in due)
            await ActivateOneAsync(item.TicketId, item.ScheduleVersion, nowUtc, ct);
    }

    private async Task ActivateOneAsync(Guid ticketId, int expectedVersion, DateTime nowUtc, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var activationService = scope.ServiceProvider.GetRequiredService<ITicketActivationService>();

        try
        {
            await uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var ticket = await uow.Tickets.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.Id == ticketId && !x.IsDeleted, transactionCt);
                if (ticket is null || ticket.ScheduleVersion != expectedVersion)
                    return;

                var primaryStaffId = await uow.TicketAssignments.GetAllAsync()
                    .Where(x => x.TicketId == ticketId && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
                    .Select(x => (Guid?)x.StaffId)
                    .SingleOrDefaultAsync(transactionCt);
                if (!primaryStaffId.HasValue)
                    return;

                var result = await activationService.ActivateAsync(new ActivationRequest(
                    ticket, primaryStaffId.Value, expectedVersion, nowUtc,
                    ActivationReason.ScheduledDue, Guid.Empty, ActorRoleEnum.System, "System"), transactionCt);
                if (result.Activated)
                    await uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogInformation(exception,
                "Ticket {TicketId} schedule version {ScheduleVersion} lost an activation race.", ticketId, expectedVersion);
        }
    }
}
