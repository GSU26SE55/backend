using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketKbReference : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid KbArticleId { get; set; }
    public string KbArticleCode { get; set; } = string.Empty;
    public Guid ReferencedByUserId { get; set; }
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }
}
