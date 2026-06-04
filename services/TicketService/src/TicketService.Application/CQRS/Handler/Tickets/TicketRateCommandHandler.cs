using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Events;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketRateCommandHandler : IRequestHandler<TicketRateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;

    public TicketRateCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
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
        var rateEvent = new TicketRatedIntegrationEvent(ticket.Id, ticket.Code, request.CustomerId, request.Rating, request.RatingComment);
        await _uow.OutboxMessages.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = ticket.Id,
            Type = nameof(TicketRatedIntegrationEvent),
            Payload = JsonSerializer.Serialize(rateEvent),
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 0
        });

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đánh giá thành công. Ticket đã được đóng.",
            Data = new TicketActionDto
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message, string field = "Ticket")
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = new List<Errors>
            {
                new Errors { Field = field, Detail = message }
            }
        };
    }
}
