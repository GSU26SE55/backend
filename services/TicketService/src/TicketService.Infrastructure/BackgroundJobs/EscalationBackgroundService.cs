using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>Escalates an eligible ticket once after the SLA timer has atomically breached.</summary>
public sealed class EscalationBackgroundService : IConsumer<SlaBreachedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IInboxStore _inboxStore;

    public EscalationBackgroundService(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter outboxWriter,
        IInboxStore inboxStore)
        => (_uow, _stateMachine, _activityLogger, _outboxWriter, _inboxStore) =
            (uow, stateMachine, activityLogger, outboxWriter, inboxStore);

    public async Task Consume(ConsumeContext<SlaBreachedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(EscalationBackgroundService), async () =>
        {
            await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var ticket = await _uow.Tickets.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.Id == context.Message.TicketId && !x.IsDeleted, ct);
                if (ticket is null || ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate ||
                    ticket.Status is not (TicketStatusEnum.Assigned or TicketStatusEnum.InProgress or TicketStatusEnum.Escalated))
                    return;

                var nextPriority = ticket.Priority switch
                {
                    TicketPriorityEnum.P3Normal => TicketPriorityEnum.P2High,
                    TicketPriorityEnum.P2High => TicketPriorityEnum.P1Critical,
                    _ => (TicketPriorityEnum?)null
                };
                if (nextPriority is null)
                {
                    // A P1 ticket cannot be raised further. Keep its operational
                    // status unchanged, but declare an incident once.
                    if (ticket.Priority != TicketPriorityEnum.P1Critical || ticket.IsIncident)
                        return;

                    ticket.IsIncident = true;
                    ticket.EscalationReason = EscalationReasonEnum.SlaBreach;
                    ticket.EscalatedAt = DateTime.UtcNow;
                    _uow.Tickets.UpdateAsync(ticket);
                    await _activityLogger.LogAsync(ticket.Id, Guid.Empty, ActorRoleEnum.System, "System",
                        ActivityActionEnum.IncidentDeclared, reason: "SLA breached while ticket is P1");
                    await _outboxWriter.WriteAsync(new IncidentDeclaredEvent(ticket.Id, ticket.Code, Guid.Empty), ct);
                    await _uow.SaveChangesAsync(ct);
                    return;
                }

                var oldPriority = ticket.Priority;
                ticket.Priority = nextPriority.Value;
                ticket.EscalationReason = EscalationReasonEnum.SlaBreach;
                ticket.EscalatedAt = DateTime.UtcNow;

                if (ticket.Status != TicketStatusEnum.Escalated)
                {
                    await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
                    {
                        ActorUserId = Guid.Empty,
                        ActorRole = ActorRoleEnum.System,
                        ActorDisplayName = "System (SLA Breach)",
                        Payload = new Dictionary<string, object?> { ["EscalationReason"] = EscalationReasonEnum.SlaBreach, ["BreachedAt"] = context.Message.BreachedAt }
                    }, ct);
                }

                _uow.Tickets.UpdateAsync(ticket);
                await _activityLogger.LogAsync(ticket.Id, Guid.Empty, ActorRoleEnum.System, "System",
                    ActivityActionEnum.Escalated, oldValue: oldPriority?.ToString(), newValue: nextPriority.Value.ToString(), reason: "SLA breached");
                await _outboxWriter.WriteAsync(new TicketEscalatedEvent(ticket.Id, ticket.Code,
                    (int)EscalationReasonEnum.SlaBreach, "SLA breached", null, "System"), ct);
                await _uow.SaveChangesAsync(ct);
            }, context.CancellationToken);
        });
    }
}
