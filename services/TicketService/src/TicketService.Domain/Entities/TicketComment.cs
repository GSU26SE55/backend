using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketComment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid AuthorUserId { get; set; }
    public ActorRoleEnum AuthorRole { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string Body { get; set; }
    public bool IsInternal { get; set; }
    public List<Guid> AttachmentFileIds { get; set; } = new();

    public Ticket Ticket { get; set; }
}
