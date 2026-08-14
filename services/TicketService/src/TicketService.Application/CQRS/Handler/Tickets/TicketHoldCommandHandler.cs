using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketHoldCommandHandler : IRequestHandler<TicketHoldCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly ISlaService _slaService;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketHoldCommandHandler(ITicketUnitOfWork uow, ITicketStateMachine stateMachine,
        IActivityLogger activityLogger, ISlaService slaService,
        IIntegrationEventOutboxWriter outboxWriter, IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _slaService = slaService;
        _outboxWriter = outboxWriter;
    }

    public async Task<TicketActionResponse> Handle(TicketHoldCommand request, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var appointmentUtc = request.RescheduledStartAt.UtcDateTime;
        if (appointmentUtc <= nowUtc)
            return Fail(400, "RescheduledStartAt must be strictly in the future.");

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");

        ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
            .Where(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
            .Select(x => (Guid?)x.StaffId)
            .SingleOrDefaultAsync(ct);
        if (ticket.PrimaryHandlerStaffId != request.StaffId)
            return Fail(403, "Only the active PrimaryHandler can hold this ticket.");
        if (ticket.Status != TicketStatusEnum.InProgress)
            return Fail(409, "Only an InProgress ticket can be held.");

        var eligibility = await _slaService.CheckPauseEligibilityAsync(ticket.Id, ct);
        if (!eligibility.IsAllowed)
            return Fail(409, eligibility.Message ?? "The SLA cannot be paused.");

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            var oldStatus = ticket.Status;
            ticket.ScheduledStartAtUtc = appointmentUtc;
            ticket.ScheduleVersion++;
            ticket.PendingContext = PendingContextEnum.Held;
            ticket.PendingReason = request.Reason;
            ticket.Reason = request.Note!.Trim();

            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Pending, new TransitionContext
            {
                ActorUserId = request.StaffId,
                ActorRole = ActorRoleEnum.Staff,
                ActorDisplayName = request.StaffName ?? "Staff",
                Payload = new() { ["Note"] = ticket.Reason }
            }, transactionCt);
            await _slaService.PauseSlaAsync(ticket.Id, request.Reason, ticket.Reason, request.StaffId, transactionCt);
            await _activityLogger.LogAsync(ticket.Id, request.StaffId, ActorRoleEnum.Staff,
                request.StaffName, ActivityActionEnum.SlaPaused, oldStatus.ToString(),
                TicketStatusEnum.Pending.ToString(), ticket.Reason);
            await _outboxWriter.WriteAsync(new TicketStatusChangedIntegrationEvent(
                ticket.Id, ticket.Code, oldStatus, TicketStatusEnum.Pending), transactionCt);
            await _outboxWriter.WriteAsync(new TicketStatusChangedEvent(
                ticket.Id, ticket.Code, ticket.CustomerId, request.StaffId,
                (int)oldStatus, (int)TicketStatusEnum.Pending,
                oldStatus.ToString(), nameof(TicketStatusEnum.Pending)), transactionCt);
            await _outboxWriter.WriteAsync(new TicketScheduleChangedEvent(
                ticket.Id, ticket.Code, ticket.CustomerId, request.StaffId,
                null, appointmentUtc, ticket.ScheduleVersion), transactionCt);
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        return Success(ticket, "Ticket held until the new appointment.");
    }

    private static TicketActionResponse Success(TicketService.Domain.Entities.Ticket ticket, string message) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = message,
        Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
    };
    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
