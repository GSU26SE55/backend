using System;
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

    // #556 — ReactionChanged
    Task NotifyReactionChangedAsync(Guid ticketId, Guid chatId, bool isInternal, TicketChatReactionsAggregateDTO reactions, CancellationToken cancellationToken = default);

    // #556 — MentionReceived (gửi tới user cụ thể)
    Task NotifyMentionReceivedAsync(Guid mentionedUserId, TicketChatDTO chat, CancellationToken cancellationToken = default);

    // #557 — Force disconnect participant bị xóa khỏi ticket
    Task ForceDisconnectFromTicketAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default);

}
