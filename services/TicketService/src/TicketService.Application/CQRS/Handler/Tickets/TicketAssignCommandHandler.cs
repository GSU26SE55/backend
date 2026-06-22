using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using SharedContracts.Events;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketAssignCommandHandler : IRequestHandler<TicketAssignCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;

    public TicketAssignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IMessageProducerService producer)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _producer = producer;
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

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Assigned, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager"
        }, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName ?? "Manager",
            ActivityActionEnum.StaffAssigned,
            oldValue: oldStaffId?.ToString(),
            newValue: request.StaffId.ToString(),
            reason: request.Notes);

        await _producer.PublishAsync(new TicketAssignedEvent(ticket.Id, ticket.Code, request.StaffId, ticket.Priority.ToString()!), ct);

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
