using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketChatRead : AuditableEntity
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public ActorRoleEnum UserRole { get; set; }
    public DateTime ReadAt { get; set; }

    public required TicketChat Chat { get; set; }
}
