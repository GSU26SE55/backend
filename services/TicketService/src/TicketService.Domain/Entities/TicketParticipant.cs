using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketParticipant : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid UserId { get; set; }
    public ActorRoleEnum UserRole { get; set; }
    public ParticipantTypeEnum ParticipantType { get; set; }
    public bool CanPost { get; set; }
    public bool CanViewInternal { get; set; }
    public Guid AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public string? RemoveReason { get; set; }

    public required Ticket Ticket { get; set; }
}
