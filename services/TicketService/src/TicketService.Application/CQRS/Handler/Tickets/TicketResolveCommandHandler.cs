using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketResolveCommandHandler : IRequestHandler<TicketResolveCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketResolveCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(TicketResolveCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (ticket.EscalatedAt.HasValue && ticket.AssignedStaffId != request.StaffId)
            return Fail(403, "Chỉ Staff đang được assign sau escalation mới có thể resolve.");

        if (ticket.EscalationReason == EscalationReasonEnum.SkillGap)
        {
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == request.StaffId && !s.IsDeleted, ct);
            if (staff == null || (int)staff.SkillTier < 2)
                return Fail(403, "Cần Staff Tier cao hơn hiện tại cho SkillGap escalation.");
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Resolved, ActorRoleEnum.Staff, request.StaffId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot resolve.");

        // TỰ ĐỘNG ĐÓNG CÁC MAINTENANCE LOG CHƯA XONG KHI RESOLVE TICKET
        var activeLogs = await _uow.MaintenanceLogs.GetAllAsync()
            .Where(m => m.TicketId == ticket.Id && m.CompletedAt == null && !m.IsDeleted)
            .ToListAsync(ct);

        foreach (var log in activeLogs)
        {
            log.CompletedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(log.Summary) || log.Summary == "Đang thực hiện...")
            {
                log.Summary = request.ResolutionSummary;
            }
        }

        ticket.ResolutionSummary = request.ResolutionSummary;
        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Resolved, new TransitionContext
        {
            ActorUserId = request.StaffId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = request.StaffName ?? "Staff",
            Payload = new Dictionary<string, object?> { { "ResolutionSummary", request.ResolutionSummary } }
        }, ct);

        var action = ticket.EscalatedAt.HasValue ? ActivityActionEnum.ResolvedByEscalatedStaff : ActivityActionEnum.Resolved;
        await _activityLogger.LogAsync(ticket.Id, request.StaffId, ActorRoleEnum.Staff, request.StaffName, action, newValue: request.ResolutionSummary);

        // Sprint 6.2 NOTI-05 (#676) — kèm CustomerId để notify Customer khi ticket được resolve.
        await _producer.PublishAsync(
            new TicketResolvedEvent(ticket.Id, ticket.Code, request.StaffId, request.ResolutionSummary, ticket.CustomerId), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.ResolutionAdded, ticket.Id, targetDisplay: ticket.Code), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket resolved.",
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
