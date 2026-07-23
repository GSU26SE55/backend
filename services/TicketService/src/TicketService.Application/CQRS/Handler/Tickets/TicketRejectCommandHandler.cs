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

public class TicketRejectCommandHandler : IRequestHandler<TicketRejectCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketRejectCommandHandler(
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

    public async Task<TicketActionResponse> Handle(TicketRejectCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (ticket.PrimaryHandlerStaffId == null && _uow.TicketAssignments != null)
        {
            ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
                .Where(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
                .Select(a => (Guid?)a.StaffId)
                .FirstOrDefaultAsync(ct);
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot reject.");

        ticket.Reason = request.Reason;
        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager",
            Payload = new Dictionary<string, object?> { { "Reason", request.Reason } }
        }, ct);

        await _activityLogger.LogAsync(ticket.Id, request.ManagerId, ActorRoleEnum.Manager, request.ManagerName, ActivityActionEnum.Rejected, reason: request.Reason);

        await _producer.PublishAsync(new TicketRejectedIntegrationEvent(ticket.Id, ticket.Code, ticket.PrimaryHandlerStaffId ?? Guid.Empty, request.Reason), ct);

        // Sprint 6.2 NOTI-07 (#678) — Manager từ chối KẾT QUẢ resolve, ticket quay lại IN_PROGRESS
        // → người cần biết là Staff đang assign (IsClosedRejected = false).
        await _producer.PublishAsync(new TicketRejectedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.AssignedStaffId, request.Reason,
            IsClosedRejected: false, DateTime.UtcNow), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.RejectedByManager, ticket.Id, targetDisplay: ticket.Code, reason: request.Reason), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket resolution rejected.",
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
