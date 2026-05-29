using TicketService.Application.StateMachine.Rules;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine;

public class TicketStateMachine : ITicketStateMachine
{
    private readonly ITransitionRuleProvider _ruleProvider;

    public TicketStateMachine(ITransitionRuleProvider ruleProvider)
    {
        _ruleProvider = ruleProvider;
    }

    public TransitionResult CanTransition(Ticket ticket, TicketStatusEnum target, ActorRoleEnum actorRole, Guid actorUserId)
    {
        // Kiểm tra ticket nếu đã closed thì ko thể thay đổi trạng thái nữa
        if (TicketStatusEnum.Closed.Equals(ticket.Status))
        {
            return new TransitionResult()
            {
                IsAllowed = false,
                Reason = "Ticket is closed. No further transitions allowed.",
            };
        }

        // Lấy danh sách các trạng thái có thể chuyển của ticket.Status
        if (!_ruleProvider.GetRules().TryGetValue(ticket.Status, out var targetRules))
        {
            return new TransitionResult
            {
                IsAllowed = false,
                Reason = $"No transitions defined from {ticket.Status}."
            };
        }

        // Lấy rule function cho target status coi có trong Dictionary ko, nếu ko thì sẽ báo lỗi
        if (!targetRules.TryGetValue(target, out var ruleFunction))
        {
            return new TransitionResult
            {
                IsAllowed = false,
                Reason = $"Cannot transition from {ticket.Status} to {target}."
            };
        }

        // Sau khi kiểm tra có thể chuyển trạng thái, kiểm tra xem bạn có quyền ko
        return ruleFunction(ticket, actorRole, actorUserId);
    }

    // Tạm thời chưa xử lý metadata và raised events, sẽ làm sau ở Issue 85, 86
    public Task<TransitionResult> ExecuteAsync(Ticket ticket, TicketStatusEnum target, TransitionContext ctx, CancellationToken ct)
    {
        // 1. Kiểm tra quyền trước
        var result = CanTransition(ticket, target, ctx.ActorRole, ctx.ActorUserId);
        if (!result.IsAllowed)
            return Task.FromResult(result);

        // 2. Lưu trạng thái cũ để xử lý metadata
        var previousStatus = ticket.Status;

        // 3. Cập nhật trạng thái
        ticket.Status = target;
        ticket.UpdatedAt = DateTime.UtcNow;


        // 4. Cập nhật metadata theo từng transition
        UpdateMetadata(ticket, previousStatus, target, ctx);

        // 5. Build RaisedEvents
        // result.RaisedEvents = BuildEvents(ticket, previousStatus, target, ctx);

        return Task.FromResult(result);
    }

    private void UpdateMetadata(Ticket ticket, TicketStatusEnum from, TicketStatusEnum to, TransitionContext ctx)
    {
        switch (to)
        {
            case TicketStatusEnum.Open:
                // Reopen → tăng ReopenCount
                if (from == TicketStatusEnum.ClosedPendingRate)
                    ticket.ReopenCount++;
                break;

            // case TicketStatusEnum.Assigned:
            //     ticket. = DateTime.UtcNow;
            //     ticket.AssignedStaffId = ctx.TargetStaffId;
            //     break;

            // case TicketStatusEnum.InProgress:
            //     ticket.Start ??= DateTime.UtcNow; // chuyen sang tinh SLA timer
            //     break;

            case TicketStatusEnum.Resolved:
                ticket.ResolvedAt = DateTime.UtcNow;
                ticket.ResolvedByStaffId = (ctx.ActorUserId == ticket.AssignedStaffId && ctx.ActorRole == ActorRoleEnum.Staff)
                                            ? ctx.ActorUserId : null;
                break;

            case TicketStatusEnum.ClosedPendingRate:
                ticket.ApprovedAt = DateTime.UtcNow;
                break;

            case TicketStatusEnum.Closed:
                ticket.ClosedAt = DateTime.UtcNow;
                break;
        }
    }
}
