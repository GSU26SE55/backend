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

public class TicketReopenCommandHandler : IRequestHandler<TicketReopenCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketReopenCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(TicketReopenCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket không tìm thấy.");

        // BR-06: Customer reopens within 7 days
        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, request.CustomerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Không thể mở lại ticket.");

        // Execute reopen transition
        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Open, new TransitionContext
        {
            ActorUserId = request.CustomerId,
            ActorRole = ActorRoleEnum.Customer,
            ActorDisplayName = request.CustomerName!,
            Payload = new Dictionary<string, object?> { { "ReopenReason", request.ReopenReason } }
        }, ct);

        await _activityLogger.LogAsync(ticket.Id, request.CustomerId, ActorRoleEnum.Customer, request.CustomerName, ActivityActionEnum.Reopened, newValue: request.ReopenReason);

        // Outbox: Ticket Reopened
        await _outboxWriter.WriteAsync(new TicketReopenedIntegrationEvent(ticket.Id, ticket.Code, request.CustomerId, request.ReopenReason), ct);

        // Sprint 6.2 NOTI-07 (#678) — bản SharedContracts cho NotificationService (notify Manager + Staff).
        await _producer.PublishAsync(new TicketReopenedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.AssignedStaffId, request.ReopenReason,
            ticket.ReopenCount, DateTime.UtcNow), ct);

        // BR-07: Auto-escalate if ReopenCount >= 2
        if (ticket.ReopenCount >= 2)
        {
            var escalateResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.System, Guid.Empty);
            if (escalateResult.IsAllowed)
            {
                var reason = EscalationReasonEnum.CustomerComplaint;
                var note = $"Tự động escalate do mở lại nhiều lần ({ticket.ReopenCount}).";

                await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
                {
                    ActorUserId = Guid.Empty,
                    ActorRole = ActorRoleEnum.System,
                    ActorDisplayName = "System",
                    Payload = new Dictionary<string, object?>
                    {
                        { "EscalationReason", reason },
                        { "Note", note }
                    }
                }, ct);

                await _activityLogger.LogAsync(ticket.Id, Guid.Empty, ActorRoleEnum.System, "System", ActivityActionEnum.Escalated, newValue: reason.ToString(), reason: note);

                await _outboxWriter.WriteAsync(new TicketEscalatedIntegrationEvent(ticket.Id, ticket.Code, reason, note, null, null), ct);
            }
        }

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.ReopenedByAdmin, ticket.Id, targetDisplay: ticket.Code), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = ticket.Status == TicketStatusEnum.Escalated ? "Ticket đã được mở lại và tự động escalate." : "Ticket đã được mở lại.",
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
