using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketChatEdit : AuditableEntity
{
    public Guid ChatId { get; set; }
    public required string OldBody { get; set; }
    public required string NewBody { get; set; }
    public DateTime EditedAt { get; set; }
    public Guid EditedByUserId { get; set; }
    public ActorRoleEnum EditedByRole { get; set; }
    public string? EditReason { get; set; }

    public required TicketChat Chat { get; set; }
}
