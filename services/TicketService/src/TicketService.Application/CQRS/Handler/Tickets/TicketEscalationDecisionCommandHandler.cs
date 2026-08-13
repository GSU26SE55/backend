using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public sealed class TicketEscalationDecisionCommandHandler : IRequestHandler<TicketEscalationDecisionCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly ISlaService _slaService;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketActivationService _slaTransitions;

    public TicketEscalationDecisionCommandHandler(ITicketUnitOfWork uow, ITicketStateMachine stateMachine,
        ISlaService slaService, IActivityLogger activityLogger, IIntegrationEventOutboxWriter outboxWriter,
        ITicketActivationService slaTransitions)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _slaService = slaService;
        _activityLogger = activityLogger;
        _outboxWriter = outboxWriter;
        _slaTransitions = slaTransitions;
    }

    public async Task<TicketActionResponse> Handle(TicketEscalationDecisionCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.Status != TicketStatusEnum.Request)
            return Fail(409, "Ticket is not awaiting an escalation decision.");

        var target = request.Approve ? TicketStatusEnum.ReAssign : TicketStatusEnum.InProgress;
        var transition = _stateMachine.CanTransition(ticket, target, ActorRoleEnum.Manager, request.ManagerId);
        if (!transition.IsAllowed)
            return Fail(409, transition.Reason ?? "Decision transition is not allowed.");

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            if (!request.Approve)
            {
                await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
                {
                    ActorUserId = request.ManagerId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = request.ManagerName ?? "Manager"
                }, transactionCt);
                await _slaService.ResumeSlaAsync(ticket.Id, request.ManagerId, transactionCt);
            }
            else
            {
                var oldPriority = ticket.Priority;
                ticket.Priority = oldPriority switch
                {
                    TicketPriorityEnum.P3Normal => TicketPriorityEnum.P2High,
                    TicketPriorityEnum.P2High => TicketPriorityEnum.P1Critical,
                    TicketPriorityEnum.P1Critical => TicketPriorityEnum.Urgent,
                    _ => throw new InvalidOperationException("Urgent tickets cannot be escalated.")
                };
                await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.ReAssign, new TransitionContext
                {
                    ActorUserId = request.ManagerId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = request.ManagerName ?? "Manager"
                }, transactionCt);

                var primary = await _uow.TicketAssignments.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler, transactionCt);
                var keepUrgentPrimary = ticket.Priority == TicketPriorityEnum.Urgent && request.KeepCurrentPrimary && primary is not null;
                if (keepUrgentPrimary)
                {
                    var staff = await _uow.StaffAccounts.GetAllAsync().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.AccountId == primary!.StaffId && !x.IsDeleted, transactionCt);
                    keepUrgentPrimary = staff is not null && AssignmentRoleHelper.ValidatePrimaryHandlerTier(TicketPriorityEnum.Urgent, staff.SkillTier);
                }
                if (primary is not null && !keepUrgentPrimary)
                {
                    primary.Role = AssignmentRoleEnum.PreviousPrimaryHandler;
                    _uow.TicketAssignments.UpdateAsync(primary);
                }

                if (ticket.Priority == TicketPriorityEnum.Urgent)
                    await _slaTransitions.StopSlaAsync(ticket, transactionCt);
                await _outboxWriter.WriteAsync(new TicketEscalatedEvent(ticket.Id, ticket.Code,
                    (int)(ticket.EscalationReason ?? EscalationReasonEnum.CustomerComplaint), request.Reason.Trim(),
                    primary?.StaffId, null), transactionCt);
            }

            ticket.Reason = request.Reason.Trim();
            await _activityLogger.LogAsync(ticket.Id, request.ManagerId, ActorRoleEnum.Manager,
                request.ManagerName, request.Approve ? ActivityActionEnum.Escalated : ActivityActionEnum.SlaResumed,
                reason: request.Reason.Trim());
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = request.Approve ? "Escalation approved; ticket requires reassignment." : "Escalation rejected; work resumed.",
            Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
        };
    }

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
