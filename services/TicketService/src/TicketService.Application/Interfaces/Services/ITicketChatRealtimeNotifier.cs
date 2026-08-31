using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.Interfaces.Services;

public interface ITicketChatRealtimeNotifier
{
    // #553 — ChatAdded
    Task NotifyChatAddedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default);

    // #556 — ChatEdited
    Task NotifyChatEditedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default);

    // #556 — ChatDeleted
    Task NotifyChatDeletedAsync(Guid ticketId, Guid chatId, string byUserDisplayName, bool isInternal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo 1 bình luận vừa được pin/unpin tới mọi người đang mở ticket.
    /// Pin là trạng thái dùng chung của cả ticket (thanh ghim trên đầu khung chat), nên người
    /// khác phải thấy ngay mà không cần F5 — giống ChatEdited/ChatDeleted.
    /// </summary>
    /// <param name="ticketId">Ticket chứa bình luận.</param>
    /// <param name="chatId">Bình luận vừa đổi trạng thái ghim.</param>
    /// <param name="isPinned">true = vừa pin, false = vừa unpin.</param>
    /// <param name="isInternal">Chọn group Internal hay Public để không lộ tin nội bộ.</param>
    /// <param name="byUserDisplayName">Người thực hiện — hiển thị trong dòng hệ thống ở FE.</param>
    /// <param name="cancellationToken">Token hủy thao tác gửi SignalR.</param>
    Task NotifyChatPinChangedAsync(
        Guid ticketId,
        Guid chatId,
        bool isPinned,
        bool isInternal,
        string byUserDisplayName,
        CancellationToken cancellationToken = default);

    // #556 — ReactionChanged
    Task NotifyReactionChangedAsync(Guid ticketId, Guid chatId, bool isInternal, TicketChatReactionsAggregateDTO reactions, CancellationToken cancellationToken = default);

    // #556 — MentionReceived (gửi tới user cụ thể)
    Task NotifyMentionReceivedAsync(Guid mentionedUserId, TicketChatDTO chat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo "đã xem" tới người GỬI của các tin vừa được đọc — nền của tick "đã xem" kiểu Messenger.
    /// Bắn tới từng tác giả (không broadcast cả group): chỉ người gửi mới quan tâm ai đã đọc tin
    /// của mình, và ai đọc gì là thông tin không nên phát cho mọi người trong ticket.
    /// </summary>
    /// <param name="ticketId">Ticket chứa các chat vừa được đọc.</param>
    /// <param name="readsByAuthor">Tác giả → danh sách receipt mới của chính tin họ gửi.</param>
    /// <param name="cancellationToken">Token hủy thao tác gửi SignalR.</param>
    Task NotifyChatReadAsync(
        Guid ticketId,
        IReadOnlyDictionary<Guid, List<ChatReaderDTO>> readsByAuthor,
        CancellationToken cancellationToken = default);

    // #557 — Force disconnect participant bị xóa khỏi ticket
    Task ForceDisconnectFromTicketAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default);

}
