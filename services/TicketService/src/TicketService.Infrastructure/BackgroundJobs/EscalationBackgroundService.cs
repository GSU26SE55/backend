using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using TicketService.Application.Common.Events;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

public class EscalationBackgroundService : IConsumer<SlaBreachedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;

    public EscalationBackgroundService(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
    }

    public async Task Consume(ConsumeContext<SlaBreachedEvent> context)
    {
        var message = context.Message;

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == message.TicketId && !t.IsDeleted, context.CancellationToken);

        if (ticket == null)
            return;

        // Per design: P1/P2 breach -> auto escalate. P3 -> only log
        if (ticket.Priority == TicketPriorityEnum.P1Critical || ticket.Priority == TicketPriorityEnum.P2High)
        {
            if (ticket.Status == TicketStatusEnum.Escalated)
                return;

            ticket.EscalationReason = EscalationReasonEnum.SlaBreach;
            ticket.EscalatedAt = DateTime.UtcNow;

            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
            {
                ActorUserId = Guid.Empty, // System
                ActorRole = ActorRoleEnum.System, // System Actor
                ActorDisplayName = "System (SLA Breach)",
                Payload = new Dictionary<string, object?> { { "EscalationReason", EscalationReasonEnum.SlaBreach }, { "BreachedAt", message.BreachedAt } }
            }, context.CancellationToken);

            await _activityLogger.LogAsync(ticket.Id, Guid.Empty, ActorRoleEnum.System, "System", ActivityActionEnum.Escalated, newValue: EscalationReasonEnum.SlaBreach.ToString(), reason: "SLA breached");

            // Outbox: Ticket Escalated
            var @event = new TicketEscalatedIntegrationEvent(ticket.Id, ticket.Code, EscalationReasonEnum.SlaBreach, "SLA breached", null, "System");
            await _uow.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                AggregateId = ticket.Id,
                Type = nameof(TicketEscalatedIntegrationEvent),
                Payload = JsonSerializer.Serialize(@event),
                OccurredAtUtc = DateTime.UtcNow,
                RetryCount = 0
            });

            await _uow.SaveChangesAsync(context.CancellationToken);
        }
    }
}
