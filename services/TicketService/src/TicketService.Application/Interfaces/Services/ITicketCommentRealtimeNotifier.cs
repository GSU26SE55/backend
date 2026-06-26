using System.Threading;
using System.Threading.Tasks;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.Interfaces.Services;

public interface ITicketCommentRealtimeNotifier
{
    Task NotifyCommentAddedAsync(TicketCommentDTO comment, CancellationToken cancellationToken = default);
}
