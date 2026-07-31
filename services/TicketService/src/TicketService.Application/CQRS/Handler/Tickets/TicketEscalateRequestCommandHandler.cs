using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
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
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketEscalateRequestCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(TicketEscalateRequestCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Staff, request.StaffId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot escalate.");

        ticket.EscalationReason = request.Reason;
        ticket.EscalatedAt = DateTime.UtcNow;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
        {
            ActorUserId = request.StaffId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = request.StaffName ?? "Staff",
            Payload = new Dictionary<string, object?> { { "EscalationReason", request.Reason }, { "Note", request.Note } }
        }, ct);

        await _activityLogger.LogAsync(ticket.Id, request.StaffId, ActorRoleEnum.Staff, request.StaffName, ActivityActionEnum.EscalationRequested, newValue: request.Reason.ToString(), reason: request.Note);

        // Auto-downgrade PrimaryHandler if tier no longer meets priority (safety check post-escalation)
        await AutoDowngradePrimaryHandlerIfNeededAsync(ticket, request.StaffId, ct);

        // Outbox: Ticket Escalated
        await _producer.PublishAsync(new TicketEscalatedIntegrationEvent(ticket.Id, ticket.Code, request.Reason, request.Note, request.StaffId, request.StaffName), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.EscalatedToManager, ticket.Id, targetDisplay: ticket.Code, reason: request.Note), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Escalation requested.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private async Task AutoDowngradePrimaryHandlerIfNeededAsync(TicketService.Domain.Entities.Ticket ticket, Guid actorId, CancellationToken ct)
    {
        if (!ticket.Priority.HasValue)
            return;

        var primaryAssignment = await _uow.TicketAssignments.GetAllAsync()
            .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler, ct);

        if (primaryAssignment == null)
            return;

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == primaryAssignment.StaffId && !s.IsDeleted, ct);

        if (staff == null || AssignmentRoleHelper.ValidatePrimaryHandlerTier(ticket.Priority.Value, staff.SkillTier))
            return;

        primaryAssignment.Role = AssignmentRoleEnum.Supporter;
        _uow.TicketAssignments.UpdateAsync(primaryAssignment);

        await _activityLogger.LogAsync(ticket.Id, actorId, ActorRoleEnum.Staff, null,
            ActivityActionEnum.StaffReassigned,
            oldValue: primaryAssignment.StaffId.ToString(),
            newValue: null,
            reason: $"Auto-downgraded: tier không đủ cho priority {ticket.Priority} sau khi escalate.");
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
