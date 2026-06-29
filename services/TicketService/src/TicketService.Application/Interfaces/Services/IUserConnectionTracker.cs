using System;
using System.Collections.Generic;

namespace TicketService.Application.Interfaces.Services;

public interface IUserConnectionTracker
{
    void Track(Guid userId, string connectionId);
    void Untrack(Guid userId, string connectionId);
    IReadOnlyCollection<string> GetConnections(Guid userId);
}
