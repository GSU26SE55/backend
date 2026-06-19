using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketResumeCommandHandler : IRequestHandler<TicketResumeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly ISlaService _slaService;
    private readonly IMessageProducerService _producer;

    public TicketResumeCommandHandler(
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

    public async Task<TicketActionResponse> Handle(TicketResumeCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, request.StaffId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot resume.");

        var oldStatus = ticket.Status;
        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
        {
            ActorUserId = request.StaffId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = request.StaffName ?? "Staff"
        }, ct);

        // SLA Timer logic
        await _slaService.ResumeSlaAsync(ticket.Id, request.StaffId, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.StaffId,
            ActorRoleEnum.Staff,
            request.StaffName ?? "Staff",
            ActivityActionEnum.SlaResumed,
            oldValue: oldStatus.ToString(),
            newValue: "InProgress");

        // Outbox: Status Changed & Ticket Resumed
        await _producer.PublishAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, oldStatus, TicketStatusEnum.InProgress), ct);
        await _producer.PublishAsync(new TicketResumedIntegrationEvent(ticket.Id, ticket.Code), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Work resumed.",
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
