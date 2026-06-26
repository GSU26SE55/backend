using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Realtime;

public class SignalRTicketCommentNotifier : ITicketCommentRealtimeNotifier
{
    private readonly IHubContext<TicketCommentHub> _hubContext;

    public SignalRTicketCommentNotifier(IHubContext<TicketCommentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyCommentAddedAsync(TicketCommentDTO comment, CancellationToken cancellationToken = default)
    {
        if (comment == null)
            return Task.CompletedTask;

        if (!Guid.TryParse(comment.TicketId, out var ticketId))
            return Task.CompletedTask;

        var targetGroup = comment.IsInternal
            ? TicketCommentHub.InternalGroup(ticketId)
            : TicketCommentHub.PublicGroup(ticketId);

        return _hubContext.Clients.Group(targetGroup).SendAsync("CommentAdded", comment, cancellationToken);
    }
}
