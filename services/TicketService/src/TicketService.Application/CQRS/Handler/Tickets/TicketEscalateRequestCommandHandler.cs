using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketEscalateRequestCommandHandler : IRequestHandler<TicketEscalateRequestCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly ISlaService _slaService;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketEscalateRequestCommandHandler(ITicketUnitOfWork uow, ITicketStateMachine stateMachine,
        IActivityLogger activityLogger, IIntegrationEventOutboxWriter outboxWriter,
        IPublisher publisher, ISlaService slaService)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = outboxWriter;
        _slaService = slaService;
    }

    public async Task<TicketActionResponse> Handle(TicketEscalateRequestCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.Priority == TicketPriorityEnum.Urgent)
            return Fail(409, "Urgent is the highest priority and cannot be escalated.");

        ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
            .Where(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
            .Select(x => (Guid?)x.StaffId)
            .SingleOrDefaultAsync(ct);
        if (ticket.PrimaryHandlerStaffId != request.StaffId)
            return Fail(403, "Only the active PrimaryHandler can request escalation.");
        if (string.IsNullOrWhiteSpace(request.Note))
            return Fail(400, "An escalation reason note is required.");

        var transition = _stateMachine.CanTransition(ticket, TicketStatusEnum.Request, ActorRoleEnum.Staff, request.StaffId);
        if (!transition.IsAllowed)
            return Fail(409, transition.Reason ?? "Escalation cannot be requested from the current status.");

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Request, new TransitionContext
            {
                ActorUserId = request.StaffId,
                ActorRole = ActorRoleEnum.Staff,
                ActorDisplayName = request.StaffName ?? "Staff"
            }, transactionCt);
            ticket.EscalationReason = request.Reason;
            ticket.EscalatedAt = DateTime.UtcNow;
            ticket.Reason = request.Note.Trim();
            await _slaService.PauseSlaAsync(ticket.Id, PauseReasonEnum.WorkBlocked,
                request.Note.Trim(), request.StaffId, transactionCt);
            await _activityLogger.LogAsync(ticket.Id, request.StaffId, ActorRoleEnum.Staff,
                request.StaffName, ActivityActionEnum.EscalationRequested, reason: request.Note.Trim());
            await _outboxWriter.WriteAsync(new TicketEscalatedIntegrationEvent(
                ticket.Id, ticket.Code, request.Reason, request.Note.Trim(), request.StaffId, request.StaffName), transactionCt);
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Escalation request submitted for Manager review.",
            Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
        };
    }

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
