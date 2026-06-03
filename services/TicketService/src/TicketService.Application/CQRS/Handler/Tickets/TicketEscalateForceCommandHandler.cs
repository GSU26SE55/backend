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

public class TicketEscalateForceCommandHandler : IRequestHandler<TicketEscalateForceCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;

    public TicketEscalateForceCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
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

        // Outbox: Status Changed (Escalated)
        var @event = new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, TicketStatusEnum.InProgress, TicketStatusEnum.Escalated);
        await _uow.OutboxMessages.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = ticket.Id,
            Type = nameof(TicketStatusChangedIntegrationEvent),
            Payload = JsonSerializer.Serialize(@event),
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 0
        });

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Escalation forced.",
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
