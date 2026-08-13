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
        if (TicketStatusEnum.ClosedRejected.Equals(ticket.Status))
        {
            return new TransitionResult()
            {
                IsAllowed = false,
                Reason = "A rejected ticket is terminal and cannot transition.",
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

    public Task<TransitionResult> ExecuteAsync(Ticket ticket, TicketStatusEnum target, TransitionContext ctx, CancellationToken ct)
    {
        // 1. Kiểm tra quyền và logic chuyển đổi một lần nữa (Gatekeeper)
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

        return Task.FromResult(result);
    }

    private void UpdateMetadata(Ticket ticket, TicketStatusEnum from, TicketStatusEnum to, TransitionContext ctx)
    {
        // Xóa lý do cũ trước khi cập nhật mới (đảm bảo Reason chỉ cho trạng thái hiện tại)
        // Nếu chuyển sang trạng thái không cần lý do, nó sẽ được để trống
        ticket.Reason = null;

        switch (to)
        {
            case TicketStatusEnum.Open:
                if (from == TicketStatusEnum.Closed)
                {
                    ticket.ReopenCount++;
                    if (ctx.Payload.TryGetValue("ReopenReason", out var reopenReason) && reopenReason is string r)
                        ticket.Reason = r;
                }
                break;


            case TicketStatusEnum.InProgress:
                ticket.PendingContext = null;
                ticket.PendingReason = null;
                if (from == TicketStatusEnum.Completed)
                {
                    if (ctx.Payload.TryGetValue("Reason", out var inProgressRejectReason) && inProgressRejectReason is string ipr)
                        ticket.Reason = ipr;
                }
                break;

            case TicketStatusEnum.Pending:
                if (ctx.Payload.TryGetValue("Note", out var note) && note is string n)
                    ticket.Reason = n;
                break;

            case TicketStatusEnum.Completed:
                ticket.ResolvedAt = DateTime.UtcNow;
                ticket.ResolvedByStaffId = (ctx.ActorUserId == ticket.PrimaryHandlerStaffId && ctx.ActorRole == ActorRoleEnum.Staff)
                                            ? ctx.ActorUserId : ticket.PrimaryHandlerStaffId;

                if (ctx.Payload.TryGetValue("ResolutionSummary", out var summary) && summary is string s)
                    ticket.ResolutionSummary = s;
                break;

            case TicketStatusEnum.ReAssign:
                ticket.EscalatedAt = DateTime.UtcNow;
                if (ctx.Payload.TryGetValue("EscalationReason", out var escalateReason) && escalateReason is EscalationReasonEnum er)
                    ticket.EscalationReason = er;
                break;

            case TicketStatusEnum.Closed:
                ticket.ApprovedAt = DateTime.UtcNow;
                ticket.ApprovedByManagerId = ctx.ActorUserId;
                ticket.ClosedAt = DateTime.UtcNow;
                if (ctx.Payload.TryGetValue("Rating", out var rating) && rating is short rate)
                {
                    ticket.Rating = rate;
                    ticket.RatedAt = DateTime.UtcNow;
                }
                if (ctx.Payload.TryGetValue("Comment", out var comment) && comment is string c)
                    ticket.RatingComment = c;
                break;

            case TicketStatusEnum.ClosedRejected:
                ticket.ClosedAt = DateTime.UtcNow;
                if (ctx.Payload.TryGetValue("Reason", out var triageRejectReason) && triageRejectReason is string trr)
                    ticket.Reason = trr;
                break;
        }
    }
}
