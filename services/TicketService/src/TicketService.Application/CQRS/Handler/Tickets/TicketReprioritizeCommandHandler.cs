using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketReprioritizeCommandHandler : IRequestHandler<TicketReprioritizeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ISlaCalculator _slaCalculator;
    private readonly IPriorityCalculator _priorityCalculator;
    private readonly IActivityLogger _activityLogger;

    // Bỏ IIntegrationEventOutboxWriter / ITicketStateMachine / IPublisher: chúng chỉ phục vụ
    // nhánh auto-escalate khi Staff không đủ tier và nhánh phát SlaBreachedEvent — cả hai đều
    // chỉ có nghĩa khi ticket ĐÃ gán Staff / đồng hồ ĐÃ chạy. Whitelist giờ chỉ còn Open nên
    // không còn đường nào chạm tới chúng.
    public TicketReprioritizeCommandHandler(
        ITicketUnitOfWork uow,
        ISlaCalculator slaCalculator,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger)
        => (_uow, _slaCalculator, _priorityCalculator, _activityLogger) =
            (uow, slaCalculator, priorityCalculator, activityLogger);

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

            if (ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate)
            {
                response = Fail(409, "Ticket is not eligible for re-prioritization.");
                return;
            }

            // Ticket đã được công bố sự cố (Declare Incident) thì mức ưu tiên Urgent là cố định.
            //
            // Declare Incident không chỉ đặt Priority = Urgent: nó dừng hẳn SLA timer
            // (Urgent không tiêu thụ SLA), chuyển PrimaryHandler sang PreviousPrimaryHandler
            // và bắn BatteryIsolationRequestedEvent để BatteryService cô lập pin. Cho hạ cấp
            // về P1/P2/P3 ở đây sẽ hồi sinh SLA timer vừa bị dừng và để lại IsIncident /
            // ActiveIncidentEpisodeId treo, trong khi pin đã bị cô lập và không có đường thu hồi.
            if (ticket.IsIncident || ticket.ActiveIncidentEpisodeId.HasValue)
            {
                response = Fail(409, "An active incident is fixed at Urgent priority and cannot be re-prioritized.");
                return;
            }

            // CHỈ Open — tức đã triage nhưng CHƯA gán Staff.
            //
            // User Guide §3.8: "Mức ưu tiên giữ cố định trong suốt vòng đời phiếu. Khi quá
            // thời hạn cam kết (SLA breach), hệ thống tự động chuyển cấp để bổ sung nhân lực,
            // chứ KHÔNG gia hạn thêm thời gian."
            //
            // Trước đây whitelist còn có Assigned/InProgress/Escalated, và nhánh dưới tính lại
            // SlaTimer.DueAt — nghĩa là Manager dời được deadline giữa lúc Staff đang làm, đúng
            // thứ tài liệu cấm. Staff thấy quá sức thì gửi Yêu cầu chuyển cấp (§3.12,
            // TicketEscalateRequestCommand) — bổ sung nhân lực, không đụng vào mức ưu tiên.
            if (ticket.Status is not TicketStatusEnum.Open)
            {
                response = Fail(409, "Priority can only be changed while the ticket has not yet been assigned to staff. Use escalation for tickets already in progress.");
                return;
            }

            var previousPriority = ticket.Priority;

            // §3.9: "Mức ưu tiên không do người nhập trực tiếp mà được suy ra từ phạm vi ảnh
            // hưởng và độ khẩn cấp" — tính qua ma trận, đồng thời lưu lại Impact/Urgency mới
            // để chúng không lệch với Priority hiển thị.
            var priority = _priorityCalculator.Calculate(request.Impact, request.Urgency);
            ticket.ImpactScope = request.Impact;
            ticket.UrgencyLevel = request.Urgency;
            ticket.Priority = priority;

            var timer = await _uow.SlaTimers.GetAllAsync()
                .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted, token);

            // Khi đổi mức ưu tiên ở Open (Stage 1), tính lại hạn chót Response SLA (24/7 calendar) theo priority mới.
            if (timer is not null)
            {
                timer.Priority = priority;
                timer.OriginalDueAt = ticket.Status == TicketStatusEnum.Open
                    ? _slaCalculator.CalculateResponseDueDate(timer.StartedAt, priority)
                    : _slaCalculator.CalculateDueDate(timer.StartedAt, priority);
                timer.DueAt = timer.OriginalDueAt;
                _uow.SlaTimers.UpdateAsync(timer);
            }

            _uow.Tickets.UpdateAsync(ticket);
            await _activityLogger.LogAsync(
                ticket.Id,
                request.ManagerId,
                ActorRoleEnum.Manager,
                request.ManagerName,
                ActivityActionEnum.PriorityAssigned,
                oldValue: previousPriority?.ToString(),
                newValue: $"{priority} (Impact: {request.Impact}, Urgency: {request.Urgency})",
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

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
