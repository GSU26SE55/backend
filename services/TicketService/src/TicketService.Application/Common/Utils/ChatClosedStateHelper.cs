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
    /// Trả về <c>null</c> nếu được phép thực hiện, ngược lại trả message lý do bị block.
    /// Customer được miễn block ở <see cref="TicketStatusEnum.ClosedPendingRate"/> cho hành động
    /// <see cref="ChatAction.Add"/> (để feedback/rating) — mọi trường hợp khác vẫn bị chặn.
    /// </summary>
    public static string? GetBlockReason(
        TicketStatusEnum ticketStatus,
        ActorRoleEnum actorRole,
        ChatAction action,
        bool blockEditOnClosed)
    {
        if (!blockEditOnClosed)
            return null;

        if (ticketStatus == TicketStatusEnum.Closed)
            return BuildMessage(action, "đã đóng");

        if (ticketStatus == TicketStatusEnum.ClosedPendingRate)
        {
            if (action == ChatAction.Add && actorRole == ActorRoleEnum.Customer)
                return null;

            return BuildMessage(action, "đang chờ đánh giá");
        }

        return null;
    }

    private static string BuildMessage(ChatAction action, string statusText) => action switch
    {
        ChatAction.Add => $"Không thể thêm bình luận khi ticket {statusText}.",
        ChatAction.Edit => $"Không thể sửa bình luận khi ticket {statusText}.",
        ChatAction.Delete => $"Không thể xóa bình luận khi ticket {statusText}.",
        _ => $"Không thể thực hiện khi ticket {statusText}."
    };
}
