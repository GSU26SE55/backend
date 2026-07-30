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

public class TicketRateCommandHandler : IRequestHandler<TicketRateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketRateCommandHandler(
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

    public async Task<TicketActionResponse> Handle(TicketRateCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket không tìm thấy.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.Customer, request.CustomerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Không thể đánh giá ticket này.");

        // Execute rate transition (to Closed)
        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Closed, new TransitionContext
        {
            ActorUserId = request.CustomerId,
            ActorRole = ActorRoleEnum.Customer,
            ActorDisplayName = request.CustomerName ?? "Customer",
            Payload = new Dictionary<string, object?>
            {
                { "Rating", request.Rating },
                { "Comment", request.RatingComment }
            }
        }, ct);

        await _activityLogger.LogAsync(ticket.Id, request.CustomerId, ActorRoleEnum.Customer, request.CustomerName, ActivityActionEnum.Rated, newValue: request.Rating.ToString(), reason: request.RatingComment);

        // Outbox: Ticket Rated
        await _producer.PublishAsync(new TicketRatedIntegrationEvent(ticket.Id, ticket.Code, request.CustomerId, request.Rating, request.RatingComment), ct);

        // Sprint 6.2 NOTI-07 (#678) — rate xong là ticket đóng hẳn (CLOSED). Event SharedContracts
        // để NotificationService xác nhận với Customer + báo Manager (IsAutoClosed = false).
        await _producer.PublishAsync(new TicketClosedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.ClosedAt ?? DateTime.UtcNow,
            IsAutoClosed: false, request.Rating), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.CustomerRated, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["rating"] = request.Rating }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đánh giá thành công. Ticket đã được đóng.",
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
