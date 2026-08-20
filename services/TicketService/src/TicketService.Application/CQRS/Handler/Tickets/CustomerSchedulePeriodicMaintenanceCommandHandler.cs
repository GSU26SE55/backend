using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public sealed class CustomerSchedulePeriodicMaintenanceCommandHandler
    : IRequestHandler<CustomerSchedulePeriodicMaintenanceCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;

    public CustomerSchedulePeriodicMaintenanceCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(
        CustomerSchedulePeriodicMaintenanceCommand request,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var scheduledStartAtUtc = request.ScheduledStartAt.UtcDateTime;
        if (scheduledStartAtUtc < nowUtc)
            return Fail(400, "ScheduledStartAt cannot be in the past.");

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);

        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.CustomerId != request.CustomerId)
            return Fail(403, "Only the owning Customer can schedule this ticket.");
        if (!ticket.PeriodicMaintenanceSourceTicketId.HasValue ||
            !ticket.PeriodicMaintenanceDueAtUtc.HasValue ||
            !ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.HasValue)
            return Fail(409, "Only a periodic-maintenance ticket can use this schedule endpoint.");
        if (ticket.Status != TicketStatusEnum.Open)
            return Fail(409, "Only an unassigned Open periodic-maintenance ticket can be scheduled by the Customer.");
        if (nowUtc > ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Value)
            return Fail(409, "The periodic-maintenance scheduling window has expired.");
        if (scheduledStartAtUtc > ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Value)
            return Fail(400, "ScheduledStartAt exceeds the periodic-maintenance scheduling deadline.");

        var previous = ticket.ScheduledStartAtUtc;
        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            ticket.ScheduledStartAtUtc = scheduledStartAtUtc;
            ticket.ScheduleVersion++;
            ticket.PeriodicMaintenanceCustomerScheduledAtUtc = nowUtc;

            var evt = new PeriodicMaintenanceScheduleChangedEvent(
                ticket.Id,
                ticket.Code,
                ticket.BatteryAssetId,
                ticket.CustomerId,
                previous,
                scheduledStartAtUtc,
                ticket.ScheduleVersion,
                nameof(ActorRoleEnum.Customer),
                request.CustomerId,
                null,
                ticket.PeriodicMaintenanceDueAtUtc.Value,
                ticket.PeriodicMaintenanceDueAtUtc.Value < nowUtc)
            {
                Id = DeterministicEventId.From(
                    ticket.Id,
                    $"periodic-maintenance-schedule:{ticket.ScheduleVersion}")
            };

            await _outboxWriter.WriteAsync(evt, transactionCt);
            await _activityLogger.LogAsync(
                ticket.Id,
                request.CustomerId,
                ActorRoleEnum.Customer,
                "Customer",
                ActivityActionEnum.PeriodicMaintenanceScheduleChanged,
                previous?.ToString("O"),
                scheduledStartAtUtc.ToString("O"));
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Periodic-maintenance schedule saved.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
