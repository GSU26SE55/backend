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
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketRateCommandHandler(
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

    public async Task<TicketActionResponse> Handle(TicketRateCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (ticket.CustomerId != request.CustomerId)
            return Fail(403, "Only the ticket owner can rate this ticket.");
        if (ticket.Status != TicketStatusEnum.Closed || ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate)
            return Fail(409, "Only a non-merged Closed ticket can be rated.");
        if (ticket.RatedAt.HasValue || ticket.Rating.HasValue)
            return Fail(409, "This ticket has already been rated.");
        if (!ticket.ClosedAt.HasValue || DateTime.UtcNow - ticket.ClosedAt.Value > TimeSpan.FromDays(7))
            return Fail(409, "The seven-day rating window has expired.");

        ticket.Rating = request.Rating;
        ticket.RatingComment = request.RatingComment;
        ticket.RatedAt = DateTime.UtcNow;

        await _activityLogger.LogAsync(ticket.Id, request.CustomerId, ActorRoleEnum.Customer, request.CustomerName, ActivityActionEnum.Rated, newValue: request.Rating.ToString(), reason: request.RatingComment);

        // Outbox: Ticket Rated
        await _outboxWriter.WriteAsync(new TicketRatedIntegrationEvent(ticket.Id, ticket.Code, request.CustomerId, request.Rating, request.RatingComment), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.CustomerRated, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["rating"] = request.Rating }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Rating submitted successfully. The ticket has been closed.",
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
