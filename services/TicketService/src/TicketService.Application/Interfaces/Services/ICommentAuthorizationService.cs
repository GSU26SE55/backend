using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TicketService.Application.Interfaces.Services;

public interface ICommentAuthorizationService
{
    Task<bool> CanAccessTicketAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default);
    bool CanViewInternalComments(IReadOnlyCollection<string> actorRoles);
}
