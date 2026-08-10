using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface ISlaService
{
    Task<SlaPauseEligibility> CheckPauseEligibilityAsync(Guid ticketId, CancellationToken ct);
    Task PauseSlaAsync(Guid ticketId, PauseReasonEnum reason, string? note, Guid userId, CancellationToken ct);
    Task ResumeSlaAsync(Guid ticketId, Guid userId, CancellationToken ct);
    Task PauseForCustomerInfoAsync(Guid ticketId, Guid chatId, Guid userId, CancellationToken ct);
    Task ResumeOnCustomerReplyAsync(Guid ticketId, Guid userId, CancellationToken ct);
}

public sealed record SlaPauseEligibility(bool IsAllowed, string? Message = null);
