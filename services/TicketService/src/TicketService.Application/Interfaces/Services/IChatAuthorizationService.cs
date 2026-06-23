using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TicketService.Application.Interfaces.Services;

public interface IChatAuthorizationService
{
    Task<bool> CanAccessTicketAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default);
    bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles);

    /// <summary>Overload có xét participant.CanViewInternal (#522) — dùng khi cần check theo ticket cụ thể.</summary>
    Task<bool> CanViewInternalChatsAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default);
}
