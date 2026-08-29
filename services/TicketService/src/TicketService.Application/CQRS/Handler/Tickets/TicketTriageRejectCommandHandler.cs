using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketTriageRejectCommandHandler : IRequestHandler<TicketTriageRejectCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketActivationService _slaTransitions;

    public TicketTriageRejectCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        ITicketActivationService slaTransitions)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _slaTransitions = slaTransitions;
    }

    public async Task<TicketActionResponse> Handle(TicketTriageRejectCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot reject ticket at this stage.");

        // Capture trạng thái cũ TRƯỚC khi ExecuteAsync mutate ticket.Status (reject hợp lệ từ New hoặc Escalated).
        var oldStatus = ticket.Status;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.ClosedRejected, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName!,
            Payload = new Dictionary<string, object?> { { "Reason", request.Reason } }
        }, ct);

        // ClosedRejected là trạng thái KẾT THÚC: không còn ai xử lý ticket nữa, nên đồng hồ SLA
        // phải dừng. Thiếu dòng này, timer ở nguyên Running và SlaTimerBackgroundService — vốn chỉ
        // lọc theo Status == Running chứ không xét trạng thái ticket — vẫn đếm ngược rồi đánh
        // Breached, bắn SlaBreachedEvent và kích hoạt escalation cho một ticket đã đóng, đồng thời
        // làm sai báo cáo SLA. TicketMergeCommandHandler và TicketDeclareIncidentCommandHandler đã
        // dừng timer ở các luồng kết thúc tương ứng của chúng.
        await _slaTransitions.StopSlaAsync(ticket, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName,
            ActivityActionEnum.Rejected,
            reason: request.Reason,
            newValue: "ClosedRejected");

        // We can use a general status changed event or a specific reject event
        await _outboxWriter.WriteAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, oldStatus, TicketStatusEnum.ClosedRejected), ct);

        // Sprint 6.2 NOTI-07 (#678) — Manager từ chối ở bước triage vì ngoài scope → CLOSED_REJECTED.
        // Người cần biết là Customer (IsClosedRejected = true).
        await _outboxWriter.WriteAsync(new TicketRejectedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.Reason,
            IsClosedRejected: true, DateTime.UtcNow), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket rejected and closed.",
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
