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

public class TicketApproveCommandHandler : IRequestHandler<TicketApproveCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketApproveCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
    }

    public async Task<TicketActionResponse> Handle(TicketApproveCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");
        if (ticket.Status != TicketStatusEnum.Completed)
            return Fail(409, "Only a Completed ticket can be approved and closed.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot approve.");

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Closed, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName!,
            Payload = new Dictionary<string, object?> { { "Comment", request.ManagerComment } }
        }, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName,
            ActivityActionEnum.Approved,
            oldValue: ticket.ActiveIncidentEpisodeId?.ToString(),
            reason: request.ManagerComment);

        await _outboxWriter.WriteAsync(new TicketApprovedIntegrationEvent(ticket.Id, ticket.Code, ticket.CustomerId), ct);

        // Sprint 6.2 NOTI-07 (#678) — event SharedContracts để NotificationService consume được
        // (event nội bộ ở trên nằm trong assembly TicketService nên service khác không bind được).
        await _outboxWriter.WriteAsync(new TicketApprovedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, request.ManagerId, request.ManagerComment,
            ticket.ApprovedAt ?? DateTime.UtcNow), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket approved.",
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
