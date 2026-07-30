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
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketAssignCommandHandler : IRequestHandler<TicketAssignCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26
    private readonly ISlaCalculator _slaCalculator;   // Sprint Bonus NS-12 (#656)

    public TicketAssignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher,
        ISlaCalculator slaCalculator)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
        _slaCalculator = slaCalculator;
    }

    public async Task<TicketActionResponse> Handle(TicketAssignCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        // Validate Staff
        var staff = (await _uow.StaffAccounts.GetAllAsync().Where(s => s.AccountId == request.StaffId).ToListAsync(ct)).FirstOrDefault();

        if (staff == null)
            return Fail(404, "Không tìm thấy thông tin nhân viên trong hệ thống Ticket.");

        if (staff.Status != AccountStatusEnum.Active)
            return Fail(403, "Tài khoản nhân viên đang bị khóa hoặc vô hiệu hóa.");

        if (!staff.IsAvailable)
            return Fail(403, "Nhân viên hiện đang không sẵn sàng nhận ticket mới.");

        // Skill Tier Enforcement/Warning
        string? warningMessage = null;
        if (ticket.EscalationReason == EscalationReasonEnum.SkillGap && staff.SkillTier < StaffSkillTierEnum.ModuleSpecialist)
        {
            return Fail(403, "Ticket này yêu cầu nhân viên có trình độ ModuleSpecialist trở lên do đang gặp SkillGap.");
        }

        if ((ticket.Category == TicketCategoryEnum.Overheat || ticket.Category == TicketCategoryEnum.Performance)
            && staff.SkillTier == StaffSkillTierEnum.Generalist)
        {
            warningMessage = "Lưu ý: Ticket thuộc danh mục phức tạp nhưng nhân viên được gán đang ở mức Generalist.";
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Transition not allowed.");

        var oldStaffId = ticket.AssignedStaffId;
        ticket.AssignedStaffId = request.StaffId;

        // Auto-tạo participant PrimaryAssignee cho Staff được gán (#528)
        await _uow.TicketParticipants.AddAsync(new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = request.StaffId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.PrimaryAssignee,
            CanPost = true,
            CanViewInternal = true,
            AddedByUserId = request.ManagerId,
            AddedAt = DateTime.UtcNow
        });

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Assigned, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager"
        }, ct);

        // Sprint Bonus NS-12 (#656, R1) — tạo SlaTimer khi ticket được Assigned (SLA start theo
        // business flow). Trước fix, timer chỉ tồn tại trong seeder → SlaTimerBackgroundService không
        // có gì đếm → không warning/breach/escalation/notify. Tạo 1 lần (idempotent).
        await EnsureSlaTimerAsync(ticket, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName ?? "Manager",
            ActivityActionEnum.StaffAssigned,
            oldValue: oldStaffId?.ToString(),
            newValue: request.StaffId.ToString(),
            reason: request.Notes);

        // Sprint 6.2 NOTI-05 (#676) — kèm CustomerId để NotificationService notify được Customer.
        await _producer.PublishAsync(
            new TicketAssignedEvent(ticket.Id, ticket.Code, request.StaffId, ticket.Priority.ToString()!, ticket.CustomerId), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["staffId"] = request.StaffId }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = warningMessage ?? "Ticket assigned successfully.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    /// <summary>
    /// Sprint Bonus NS-12 (#656, R1) — tạo SlaTimer Running nếu ticket có Priority và chưa có timer.
    /// StartedAt = now (thời điểm Assigned); DueAt theo priority (P1=4h/P2=24h/P3=72h). Idempotent
    /// để reassign không tạo timer thứ 2.
    /// </summary>
    private async Task EnsureSlaTimerAsync(TicketService.Domain.Entities.Ticket ticket, CancellationToken ct)
    {
        if (ticket.Priority is null)
            return;

        var hasTimer = await _uow.SlaTimers.GetAllAsync()
            .AnyAsync(t => t.TicketId == ticket.Id, ct);
        if (hasTimer)
            return;

        var now = DateTime.UtcNow;
        var dueAt = _slaCalculator.CalculateDueDate(now, ticket.Priority.Value);

        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = ticket.Priority.Value,
            StartedAt = now,
            DueAt = dueAt,
            OriginalDueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        });
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
