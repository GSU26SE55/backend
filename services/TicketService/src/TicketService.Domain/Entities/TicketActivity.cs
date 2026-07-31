using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketActivity : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid? SourceTicketId { get; set; }
    public Guid? ActorUserId { get; set; }
    public ActorRoleEnum ActorRole { get; set; }
    public string? ActorDisplayName { get; set; }
    public ActivityActionEnum Action { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }

    public required Ticket Ticket { get; set; }
}
