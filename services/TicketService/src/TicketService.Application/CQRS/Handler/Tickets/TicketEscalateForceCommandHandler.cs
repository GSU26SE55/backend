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

public class TicketEscalateForceCommandHandler : IRequestHandler<TicketEscalateForceCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketEscalateForceCommandHandler(
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

    public async Task<TicketActionResponse> Handle(TicketEscalateForceCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot force escalate.");

        ticket.EscalationReason = request.Reason;
        ticket.EscalatedAt = DateTime.UtcNow;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager",
            Payload = new Dictionary<string, object?> { { "EscalationReason", request.Reason }, { "Note", request.Note }, { "Forced", true } }
        }, ct);

        await _activityLogger.LogAsync(ticket.Id, request.ManagerId, ActorRoleEnum.Manager, request.ManagerName, ActivityActionEnum.Escalated, newValue: request.Reason.ToString(), reason: request.Note);

        // Outbox: Ticket Escalated
        await _producer.PublishAsync(new TicketEscalatedIntegrationEvent(ticket.Id, ticket.Code, request.Reason, request.Note, request.ManagerId, request.ManagerName), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.EscalatedToAdmin, ticket.Id, targetDisplay: ticket.Code, reason: request.Note), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Escalation forced.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
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
