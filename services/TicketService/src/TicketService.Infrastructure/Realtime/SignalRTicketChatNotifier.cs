using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Realtime;

public class SignalRTicketChatNotifier : ITicketChatRealtimeNotifier
{
    private readonly IHubContext<TicketChatHub> _hubContext;
    private readonly IUserConnectionTracker _connectionTracker;

    public SignalRTicketChatNotifier(
        IHubContext<TicketChatHub> hubContext,
        IUserConnectionTracker connectionTracker)
    {
        _hubContext = hubContext;
        _connectionTracker = connectionTracker;
    }

    public Task NotifyChatAddedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default)
    {
        if (chat == null || !Guid.TryParse(chat.TicketId, out var ticketId))
            return Task.CompletedTask;

        var group = chat.IsInternal
            ? TicketChatHub.InternalGroup(ticketId)
            : TicketChatHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(group).SendAsync("ChatAdded", chat, cancellationToken);
    }

    public Task NotifyChatEditedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default)
    {
        if (chat == null || !Guid.TryParse(chat.TicketId, out var ticketId))
            return Task.CompletedTask;

        var group = chat.IsInternal
            ? TicketChatHub.InternalGroup(ticketId)
            : TicketChatHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(group).SendAsync("ChatEdited", chat, cancellationToken);
    }

    public Task NotifyChatDeletedAsync(Guid ticketId, Guid chatId, string byUserDisplayName, bool isInternal, CancellationToken cancellationToken = default)
    {
        var group = isInternal
            ? TicketChatHub.InternalGroup(ticketId)
            : TicketChatHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(group).SendAsync("ChatDeleted", new
        {
            chatId = chatId.ToString(),
            byUserDisplayName
        }, cancellationToken);
    }

    public Task NotifyReactionChangedAsync(Guid ticketId, Guid chatId, bool isInternal, TicketChatReactionsAggregateDTO reactions, CancellationToken cancellationToken = default)
    {
        var group = isInternal
            ? TicketChatHub.InternalGroup(ticketId)
            : TicketChatHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(group).SendAsync("ReactionChanged", new
        {
            chatId = chatId.ToString(),
            reactions
        }, cancellationToken);
    }

    public Task NotifyMentionReceivedAsync(Guid mentionedUserId, TicketChatDTO chat, CancellationToken cancellationToken = default)
    {
        if (chat == null)
            return Task.CompletedTask;

        return _hubContext.Clients.User(mentionedUserId.ToString()).SendAsync("MentionReceived", chat, cancellationToken);
    }

    public async Task ForceDisconnectFromTicketAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default)
    {
        var connectionIds = _connectionTracker.GetConnections(userId);
        if (connectionIds.Count == 0)
            return;

        var publicGroup = TicketChatHub.PublicGroup(ticketId);
        var internalGroup = TicketChatHub.InternalGroup(ticketId);

        foreach (var connId in connectionIds)
        {
            await _hubContext.Groups.RemoveFromGroupAsync(connId, publicGroup, cancellationToken);
            await _hubContext.Groups.RemoveFromGroupAsync(connId, internalGroup, cancellationToken);
        }
    }

    public Task NotifySentimentAlertAsync(Guid ticketId, double score, string label, CancellationToken cancellationToken = default)
        => _hubContext.Clients
            .Group(TicketChatHub.InternalGroup(ticketId))
            .SendAsync("SentimentAlert", new { ticketId = ticketId.ToString(), score, label }, cancellationToken);
}
