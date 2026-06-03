using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Helpers;

public class ActivityLogger : IActivityLogger
{
    private readonly ITicketUnitOfWork _uow;

    public ActivityLogger(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task LogAsync(Guid ticketId, Guid? actorUserId, ActorRoleEnum actorRole, string? actorDisplayName, ActivityActionEnum action, string? oldValue = null, string? newValue = null, string? reason = null)
    {
        var activity = new TicketActivity
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            ActorDisplayName = actorDisplayName,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason,
            Ticket = null! // EF will use TicketId for the relationship
        };

        await _uow.TicketActivities.AddAsync(activity);
    }
}
