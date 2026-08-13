using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

public class EscalationBackgroundService : IConsumer<SlaBreachedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IInboxStore _inboxStore;
    private readonly ITicketActivationService _slaTransitions;

    public EscalationBackgroundService(ITicketUnitOfWork uow, ITicketStateMachine stateMachine,
        IActivityLogger activityLogger, IIntegrationEventOutboxWriter outboxWriter, IInboxStore inboxStore,
        ITicketActivationService slaTransitions) =>
        (_uow, _stateMachine, _activityLogger, _outboxWriter, _inboxStore, _slaTransitions) =
        (uow, stateMachine, activityLogger, outboxWriter, inboxStore, slaTransitions);

    public Task Consume(ConsumeContext<SlaBreachedEvent> context) =>
        context.ProcessOnceAsync(_inboxStore, nameof(EscalationBackgroundService), async () =>
        {
            await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var ticket = await _uow.Tickets.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.Id == context.Message.TicketId && !x.IsDeleted, ct);
                if (ticket is null || ticket.Status != TicketStatusEnum.InProgress ||
                    ticket.Priority is null or TicketPriorityEnum.Urgent ||
                    ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate)
                    return;

                var timer = await _uow.SlaTimers.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, ct);
                if (timer?.Status != SlaTimerStatusEnum.Breached)
                    return;

                var oldPriority = ticket.Priority.Value;
                var nextPriority = oldPriority switch
                {
                    TicketPriorityEnum.P3Normal => TicketPriorityEnum.P2High,
                    TicketPriorityEnum.P2High => TicketPriorityEnum.P1Critical,
                    TicketPriorityEnum.P1Critical => TicketPriorityEnum.Urgent,
                    _ => throw new InvalidOperationException("Unsupported priority escalation.")
                };
                ticket.Priority = nextPriority;
                ticket.EscalationReason = EscalationReasonEnum.SlaBreach;
                ticket.EscalatedAt = context.Message.BreachedAt;
                await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.ReAssign, new TransitionContext
                {
                    ActorUserId = Guid.Empty,
                    ActorRole = ActorRoleEnum.System,
                    ActorDisplayName = "System (SLA breach)"
                }, ct);

                var primary = await _uow.TicketAssignments.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler, ct);
                if (nextPriority != TicketPriorityEnum.Urgent && primary is not null)
                {
                    primary.Role = AssignmentRoleEnum.PreviousPrimaryHandler;
                    _uow.TicketAssignments.UpdateAsync(primary);
                }

                if (nextPriority == TicketPriorityEnum.Urgent)
                {
                    ticket.IsIncident = true;
                    ticket.ActiveIncidentEpisodeId ??= Guid.NewGuid();
                    await _slaTransitions.StopSlaAsync(ticket, ct);
                    var assets = await _uow.TicketBatteryAssets.GetAllAsync().AsNoTracking()
                        .Where(x => x.TicketId == ticket.Id && !x.IsDeleted)
                        .Select(x => x.BatteryAssetId).ToListAsync(ct);
                    if (ticket.BatteryAssetId != Guid.Empty && !assets.Contains(ticket.BatteryAssetId))
                        assets.Add(ticket.BatteryAssetId);
                    await _outboxWriter.WriteAsync(new BatteryIsolationRequestedEvent(
                        ticket.ActiveIncidentEpisodeId.Value, ticket.Id, assets, context.Message.BreachedAt)
                    { Id = DeterministicEventId.From(ticket.ActiveIncidentEpisodeId.Value, "battery-isolation-requested") }, ct);
                }

                await _activityLogger.LogAsync(ticket.Id, Guid.Empty, ActorRoleEnum.System, "System",
                    nextPriority == TicketPriorityEnum.Urgent ? ActivityActionEnum.IncidentDeclared : ActivityActionEnum.Escalated,
                    oldPriority.ToString(), nextPriority.ToString(), "SLA breached.");
                await _outboxWriter.WriteAsync(new TicketEscalatedEvent(ticket.Id, ticket.Code,
                    (int)EscalationReasonEnum.SlaBreach, "SLA breached.", primary?.StaffId, null), ct);
                await _uow.SaveChangesAsync(ct);
            }, context.CancellationToken);
        });
}
