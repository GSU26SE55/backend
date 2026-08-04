using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketChatMention : AuditableEntity
{
    public Guid ChatId { get; set; }
    public Guid MentionedUserId { get; set; }
    public ActorRoleEnum MentionedUserRole { get; set; }
    public string? MentionedDisplayName { get; set; }

    public required TicketChat Chat { get; set; }
}
