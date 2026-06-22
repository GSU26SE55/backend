using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Realtime;

public class SignalRTicketChatNotifier : ITicketChatRealtimeNotifier
{
    private readonly IHubContext<TicketChatHub> _hubContext;

    public SignalRTicketChatNotifier(IHubContext<TicketChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyChatAddedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default)
    {
        if (chat == null)
            return Task.CompletedTask;

        if (!Guid.TryParse(chat.TicketId, out var ticketId))
            return Task.CompletedTask;

        var targetGroup = chat.IsInternal
            ? TicketChatHub.InternalGroup(ticketId)
            : TicketChatHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(targetGroup).SendAsync("ChatAdded", chat, cancellationToken);
    }
}
