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
    /// Closed and ClosedRejected are terminal and block chat mutation.
    /// </summary>
    public static string? GetBlockReason(
        TicketStatusEnum ticketStatus,
        ActorRoleEnum actorRole,
        ChatAction action,
        bool blockEditOnClosed)
    {
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
