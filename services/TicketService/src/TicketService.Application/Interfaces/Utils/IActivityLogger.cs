using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface IActivityLogger
{
    Task LogAsync(Guid ticketId, Guid? actorUserId, ActorRoleEnum actorRole, string? actorDisplayName, ActivityActionEnum action, string? oldValue = null, string? newValue = null, string? reason = null);
}
