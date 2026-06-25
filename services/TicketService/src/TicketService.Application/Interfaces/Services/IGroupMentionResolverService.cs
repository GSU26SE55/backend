using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IGroupMentionResolverService
{
    Task<List<(Guid UserId, ActorRoleEnum Role, string? DisplayName)>> ResolveAsync(
        GroupMentionInput input,
        List<TicketParticipant> activeParticipants,
        CancellationToken ct);
}
