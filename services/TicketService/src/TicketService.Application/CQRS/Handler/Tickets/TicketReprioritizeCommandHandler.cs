using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public sealed class TicketReprioritizeCommandHandler : IRequestHandler<TicketReprioritizeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ISlaCalculator _slaCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IPublisher _publisher;

    public TicketReprioritizeCommandHandler(
        ITicketUnitOfWork uow,
        ISlaCalculator slaCalculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketStateMachine stateMachine,
        IPublisher publisher)
        => (_uow, _slaCalculator, _activityLogger, _outboxWriter, _stateMachine, _publisher) =
            (uow, slaCalculator, activityLogger, outboxWriter, stateMachine, publisher);

    public async Task<TicketActionResponse> Handle(TicketReprioritizeCommand request, CancellationToken ct)
    {
        TicketActionResponse? response = null;

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var ticket = await _uow.Tickets.GetAllAsync()
                .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, token);

            if (ticket is null)
            {
                response = Fail(404, "Ticket not found.");
                return;
            }

            if (ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate ||
                ticket.Status is TicketStatusEnum.New or TicketStatusEnum.Resolved or TicketStatusEnum.Closed)
            {
                response = Fail(409, "Ticket is not eligible for re-prioritization.");
                return;
            }

            if (ticket.Status is not (TicketStatusEnum.Open or TicketStatusEnum.Assigned or TicketStatusEnum.InProgress or TicketStatusEnum.Escalated))
            {
                response = Fail(409, "Ticket status does not allow re-prioritization.");
                return;
            }

            var previousPriority = ticket.Priority;
            ticket.Priority = request.Priority;

            var timer = await _uow.SlaTimers.GetAllAsync()
                .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, token);

            if (timer is not null && timer.Status != SlaTimerStatusEnum.Breached)
            {
                timer.Priority = request.Priority;
                timer.OriginalDueAt = _slaCalculator.CalculateDueDate(timer.StartedAt, request.Priority);
                timer.DueAt = timer.OriginalDueAt.AddMinutes(timer.TotalPausedMinutes);

                if (timer.Status == SlaTimerStatusEnum.Running && timer.DueAt <= DateTime.UtcNow)
                {
                    timer.Status = SlaTimerStatusEnum.Breached;
                    timer.BreachAt = DateTime.UtcNow;
                    await _outboxWriter.WriteAsync(new SlaBreachedEvent
                    {
                        TicketId = ticket.Id,
                        BreachedAt = timer.BreachAt.Value,
                        Priority = request.Priority.ToString(),
                        Code = ticket.Code
                    }, token);
                }

                _uow.SlaTimers.UpdateAsync(timer);
            }

            await EscalateForInsufficientPrimaryTierAsync(ticket, request, token);

            _uow.Tickets.UpdateAsync(ticket);
            await _activityLogger.LogAsync(
                ticket.Id,
                request.ManagerId,
                ActorRoleEnum.Manager,
                request.ManagerName,
                ActivityActionEnum.PriorityAssigned,
                oldValue: previousPriority?.ToString(),
                newValue: request.Priority.ToString(),
                reason: request.Reason);

            await _uow.SaveChangesAsync(token);

            response = new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Ticket priority updated.",
                Data = new TicketActionDTO
                {
                    Id = ticket.Id.ToString(),
                    Code = ticket.Code,
                    Status = ticket.Status
                }
            };
        }, ct);

        return response ?? Fail(500, "Unable to re-prioritize ticket.");
    }

    private async Task EscalateForInsufficientPrimaryTierAsync(
        TicketService.Domain.Entities.Ticket ticket,
        TicketReprioritizeCommand request,
        CancellationToken ct)
    {
        var primaryAssignment = await _uow.TicketAssignments.GetAllAsync()
            .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler, ct);

        if (primaryAssignment is null)
        {
            return;
        }

        var primaryStaff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == primaryAssignment.StaffId && !s.IsDeleted, ct);

        if (primaryStaff is not null && AssignmentRoleHelper.ValidatePrimaryHandlerTier(request.Priority, primaryStaff.SkillTier))
        {
            return;
        }

        if (ticket.Status != TicketStatusEnum.Escalated)
        {
            var transition = _stateMachine.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Manager, request.ManagerId);
            if (!transition.IsAllowed)
            {
                throw new InvalidOperationException(transition.Reason ?? "Ticket cannot be escalated for a tier mismatch.");
            }

            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Escalated, new TransitionContext
            {
                ActorUserId = request.ManagerId,
                ActorRole = ActorRoleEnum.Manager,
                ActorDisplayName = request.ManagerName!,
                Payload = new Dictionary<string, object?>
                {
                    ["EscalationReason"] = EscalationReasonEnum.SkillGap
                }
            }, ct);
        }
        else
        {
            ticket.EscalationReason = EscalationReasonEnum.SkillGap;
            ticket.EscalatedAt ??= DateTime.UtcNow;
        }

        primaryAssignment.Role = AssignmentRoleEnum.PreviousPrimaryHandler;
        _uow.TicketAssignments.UpdateAsync(primaryAssignment);

        var participant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == ticket.Id && p.UserId == primaryAssignment.StaffId &&
                                      p.ParticipantType == ParticipantTypeEnum.PrimaryAssignee &&
                                      p.RemovedAt == null && !p.IsDeleted, ct);

        if (participant is not null)
        {
            participant.ParticipantType = ParticipantTypeEnum.Collaborator;
            participant.CanPost = true;
            participant.CanViewInternal = true;
            _uow.TicketParticipants.UpdateAsync(participant);
        }

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName,
            ActivityActionEnum.StaffReassigned,
            oldValue: primaryAssignment.StaffId.ToString(),
            newValue: null,
            reason: $"Primary ownership was transferred to the PreviousPrimaryHandler support role because the re-prioritized ticket requires {AssignmentRoleHelper.GetTierRequirementMessage(request.Priority)}. Manager must assign a qualified primary handler.");

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName,
            ActivityActionEnum.Escalated,
            oldValue: null,
            newValue: EscalationReasonEnum.SkillGap.ToString(),
            reason: request.Reason);

        await _outboxWriter.WriteAsync(
            new TicketEscalatedIntegrationEvent(
                ticket.Id,
                ticket.Code,
                EscalationReasonEnum.SkillGap,
                "Primary ownership was removed due to a higher required skill tier; the former primary handler remains an active PreviousPrimaryHandler supporter.",
                primaryAssignment.StaffId,
                primaryStaff?.FullName),
            ct);

        await _publisher.Publish(
            TicketAuditTrailNotification.For(
                TicketAuditActionEnum.EscalatedToManager,
                ticket.Id,
                targetDisplay: ticket.Code,
                reason: "A qualified primary handler must be assigned after re-prioritization.",
                metadata: new Dictionary<string, object?>
                {
                    ["previousPrimaryHandlerId"] = primaryAssignment.StaffId,
                    ["requiredTier"] = AssignmentRoleHelper.GetTierRequirementMessage(request.Priority)
                }),
            ct);
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
