using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Generates a PDF stream for a ticket's chat history. #568.
/// </summary>
public interface IPdfExporter
{
    /// <param name="ticketId">Ticket to export.</param>
    /// <param name="viewerRole">Controls visibility of internal chats (Customer cannot see IsInternal=true).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PDF byte stream.</returns>
    Task<Stream> ExportChatPdfAsync(Guid ticketId, ActorRoleEnum viewerRole, CancellationToken ct);
}
