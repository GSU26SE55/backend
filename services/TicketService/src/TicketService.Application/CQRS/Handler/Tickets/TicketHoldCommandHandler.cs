using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketHoldCommandHandler : IRequestHandler<TicketHoldCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly ISlaService _slaService;
    private readonly IMessageProducerService _producer;

    public TicketHoldCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        ISlaService slaService,
        IMessageProducerService producer)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _slaService = slaService;
        _producer = producer;
    }

    public async Task<TicketActionResponse> Handle(TicketHoldCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var targetStatus = request.Reason switch
        {
            PauseReasonEnum.WaitingCustomer => TicketStatusEnum.WaitingCustomer,
            PauseReasonEnum.WaitingParts => TicketStatusEnum.WaitingParts,
            PauseReasonEnum.WaitingOnsiteSchedule => TicketStatusEnum.WaitingOnsiteSchedule,
            _ => throw new ArgumentOutOfRangeException()
        };

        var transitionResult = _stateMachine.CanTransition(ticket, targetStatus, ActorRoleEnum.Staff, request.StaffId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot put on hold.");

        var oldStatus = ticket.Status;
        await _stateMachine.ExecuteAsync(ticket, targetStatus, new TransitionContext
        {
            ActorUserId = request.StaffId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = request.StaffName ?? "Staff",
            Payload = new Dictionary<string, object?> { { "Reason", request.Reason }, { "Note", request.Note } }
        }, ct);

        // SLA Timer logic
        await _slaService.PauseSlaAsync(ticket.Id, request.Reason, request.Note, request.StaffId, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.StaffId,
            ActorRoleEnum.Staff,
            request.StaffName ?? "Staff",
            ActivityActionEnum.SlaPaused,
            oldValue: oldStatus.ToString(),
            newValue: targetStatus.ToString(),
            reason: request.Note);

        // Outbox: Status Changed & Ticket Held
        await _producer.PublishAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, oldStatus, targetStatus), ct);
        await _producer.PublishAsync(new TicketHeldIntegrationEvent(ticket.Id, ticket.Code, request.Reason, request.Note), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket put on hold.",
            Data = new TicketActionDto
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message, string field = "TicketId")
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
