using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketScheduleCommandHandler : IRequestHandler<TicketScheduleCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketActivationService _activationService;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly int _currentWindowMinutes;

    public TicketScheduleCommandHandler(
        ITicketUnitOfWork uow,
        ITicketActivationService activationService,
        IIntegrationEventOutboxWriter outboxWriter,
        IOptions<TicketScheduleOptions>? scheduleOptions = null)
    {
        _uow = uow;
        _activationService = activationService;
        _outboxWriter = outboxWriter;
        _currentWindowMinutes = scheduleOptions?.Value.CurrentWindowMinutes ?? 5;
    }

    public async Task<TicketActionResponse> Handle(TicketScheduleCommand request, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var schedule = TicketScheduleClassifier.Classify(request.ScheduledStartAt, nowUtc, _currentWindowMinutes);
        if (schedule.Kind == ScheduleKind.InvalidPast)
            return Fail(400, "ScheduledStartAt cannot be older than the five-minute current window.");

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.Status != TicketStatusEnum.Pending || ticket.PendingContext != PendingContextEnum.Scheduled)
            return Fail(409, "Only a scheduled Pending ticket can be rescheduled by Manager.");

        var primaryStaffId = await _uow.TicketAssignments.GetAllAsync()
            .Where(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
            .Select(x => (Guid?)x.StaffId).SingleOrDefaultAsync(ct);
        if (!primaryStaffId.HasValue)
            return Fail(409, "The ticket has no active PrimaryHandler.");

        var previous = ticket.ScheduledStartAtUtc;
        ActivationResult? activation = null;
        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            ticket.ScheduledStartAtUtc = schedule.ScheduledStartAtUtc;
            ticket.ScheduleVersion++;
            await _outboxWriter.WriteAsync(new TicketScheduleChangedEvent(ticket.Id, ticket.Code,
                ticket.CustomerId, primaryStaffId.Value, previous, schedule.ScheduledStartAtUtc,
                ticket.ScheduleVersion), transactionCt);

            if (schedule.Kind == ScheduleKind.Current)
            {
                activation = await _activationService.ActivateAsync(new ActivationRequest(
                    ticket, primaryStaffId.Value, ticket.ScheduleVersion, nowUtc,
                    ActivationReason.Immediate, request.ManagerId, ActorRoleEnum.Manager,
                    request.ManagerName ?? "Manager"), transactionCt);
            }
            if (activation is null || activation.Activated)
                await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        if (activation is { Activated: false })
            return Fail(409, activation.Conflict ?? "The schedule could not be activated.");

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = schedule.Kind == ScheduleKind.Current ? "Schedule activated; work started." : "Ticket schedule updated.",
            Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
        };
    }

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
