using System.Threading;
using System.Threading.Tasks;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.Interfaces.Services;

public interface ITicketChatRealtimeNotifier
{
    Task NotifyChatAddedAsync(TicketChatDTO chat, CancellationToken cancellationToken = default);
}
