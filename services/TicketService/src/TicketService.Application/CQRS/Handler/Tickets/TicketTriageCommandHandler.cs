using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketTriageCommandHandler : IRequestHandler<TicketTriageCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IPriorityCalculator _priorityCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;

    public TicketTriageCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger,
        IMessageProducerService producer)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _priorityCalculator = priorityCalculator;
        _activityLogger = activityLogger;
        _producer = producer;
    }

    public async Task<TicketActionResponse> Handle(TicketTriageCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Approved, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot triage approve.");

        // Calculate priority
        var priority = request.ManualPriority ?? _priorityCalculator.Calculate(request.Impact, request.Urgency);

        ticket.ImpactScope = request.Impact;
        ticket.UrgencyLevel = request.Urgency;
        ticket.Priority = priority;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Approved, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager",
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
            request.ManagerName,
            ActivityActionEnum.TriageApproved,
            reason: request.ManagerComment,
            newValue: $"Priority: {priority}, Impact: {request.Impact}, Urgency: {request.Urgency}");

        await _producer.PublishAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, TicketStatusEnum.Open, TicketStatusEnum.Approved), ct);

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
