using System;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

/// <summary>
/// Gate chung cho việc block Add/Edit/Delete chat theo trạng thái ticket (#517) —
/// dùng chung cho 3 handler để tránh lặp logic.
/// </summary>
public static class ChatClosedStateHelper
{
    public enum ChatAction
    {
        Add,
        Edit,
        Delete
    }

    /// <summary>
    /// Các trạng thái KHÔNG cho phép gửi tin nhắn mới.
    ///
    /// Open: ticket vừa tạo, chưa qua triage nên chưa ai được giao — tin gửi lúc đó không có
    /// người nhận cụ thể, không sinh được thông báo cho đúng người và dễ trôi mất
    /// ("nhắn trước nhưng không ai thấy").
    /// Closed/ClosedRejected: ticket đã đóng, hội thoại chỉ còn để đọc lại.
    ///
    /// Pending vẫn mở: ticket đã được giao và có lịch hẹn, hai bên cần trao đổi trước khi bắt
    /// đầu xử lý. Completed cũng mở để còn trao đổi lúc nghiệm thu.
    /// </summary>
    private static readonly TicketStatusEnum[] ChatDisabledStatuses =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Closed,
        TicketStatusEnum.ClosedRejected,
    };

    /// <summary>Ticket ở trạng thái này thì được phép gửi tin nhắn mới hay không.</summary>
    public static bool IsChatEnabled(TicketStatusEnum ticketStatus)
        => Array.IndexOf(ChatDisabledStatuses, ticketStatus) < 0;

    /// <summary>
    /// Trả về <c>null</c> nếu được phép thực hiện, ngược lại trả message lý do bị block.
    /// Closed and ClosedRejected are terminal and block chat mutation.
    /// </summary>
    public static string? GetBlockReason(
        TicketStatusEnum ticketStatus,
        ActorRoleEnum actorRole,
        ChatAction action,
        bool blockEditOnClosed)
    {
        // Chỉ áp cho Add. Sửa/xoá tin CŨ ở ticket chưa được giao vẫn hợp lệ — chặn luôn thì
        // một tin lỡ gửi trước khi luật này có hiệu lực sẽ không bao giờ sửa/xoá được.
        if (action == ChatAction.Add && !IsChatEnabled(ticketStatus))
            return ticketStatus == TicketStatusEnum.Open
                ? "Chat opens once the ticket has been assigned."
                : BuildMessage(action, "closed");

        if (!blockEditOnClosed)
            return null;

        if (ticketStatus is TicketStatusEnum.Closed or TicketStatusEnum.ClosedRejected)
            return BuildMessage(action, "closed");

        return null;
    }

    private static string BuildMessage(ChatAction action, string statusText) => action switch
    {
        ChatAction.Add => $"Cannot add a comment while the ticket is {statusText}.",
        ChatAction.Edit => $"Cannot edit a comment while the ticket is {statusText}.",
        ChatAction.Delete => $"Cannot delete a comment while the ticket is {statusText}.",
        _ => $"Cannot perform this action while the ticket is {statusText}."
    };
}
