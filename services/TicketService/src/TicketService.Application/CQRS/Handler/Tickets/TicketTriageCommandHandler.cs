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

public class TicketTriageCommandHandler : IRequestHandler<TicketTriageCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IPriorityCalculator _priorityCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketTriageCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _priorityCalculator = priorityCalculator;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
    }

    public async Task<TicketActionResponse> Handle(TicketTriageCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot triage approve.");

        // Calculate priority
        var priority = request.ManualPriority ?? _priorityCalculator.Calculate(request.Impact, request.Urgency);

        ticket.ImpactScope = request.Impact;
        ticket.UrgencyLevel = request.Urgency;
        ticket.Priority = priority;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Open, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName!,
            Payload = new Dictionary<string, object?>
            {
                { "Comment", request.ManagerComment },
                { "Impact", request.Impact },
                { "Urgency", request.Urgency },
                { "Priority", priority }
            }
        }, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName!,
            ActivityActionEnum.TriageApproved,
            reason: request.ManagerComment,
            newValue: $"Priority: {priority}, Impact: {request.Impact}, Urgency: {request.Urgency}");

        await _outboxWriter.WriteAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, TicketStatusEnum.New, TicketStatusEnum.Open), ct);

        // Sprint 6.2 NOTI-07 (#678) — bản SharedContracts để Customer biết ticket đã được duyệt
        // và gán ưu tiên (bước triage), chờ phân công Staff.
        await _outboxWriter.WriteAsync(new TicketStatusChangedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.PrimaryHandlerStaffId,
            (int)TicketStatusEnum.New, (int)TicketStatusEnum.Open,
            nameof(TicketStatusEnum.New), nameof(TicketStatusEnum.Open)), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket triaged and approved for assignment.",
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
