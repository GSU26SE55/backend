using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Helpers;

public interface ISlaService
{
    Task PauseSlaAsync(Guid ticketId, PauseReasonEnum reason, string? note, Guid userId, CancellationToken ct);
    Task ResumeSlaAsync(Guid ticketId, Guid userId, CancellationToken ct);
    Task PauseForCustomerInfoAsync(Guid ticketId, Guid chatId, Guid userId, CancellationToken ct);
    Task ResumeOnCustomerReplyAsync(Guid ticketId, Guid userId, CancellationToken ct);
}
