using System;
using System.Collections.Generic;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Realtime;

public class InMemoryUserConnectionTracker : IUserConnectionTracker
{
    private readonly Dictionary<Guid, HashSet<string>> _connections = new();
    private readonly object _lock = new();

    public void Track(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set))
            {
                set = new HashSet<string>();
                _connections[userId] = set;
            }
            set.Add(connectionId);
        }
    }

    public void Untrack(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(userId, out var set))
            {
                set.Remove(connectionId);
                if (set.Count == 0)
                    _connections.Remove(userId);
            }
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(userId, out var set))
                return new List<string>(set);
            return Array.Empty<string>();
        }
    }
}
