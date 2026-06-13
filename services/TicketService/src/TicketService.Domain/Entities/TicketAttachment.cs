using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketAttachment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid FileId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? PublicUrl { get; set; }
    public AttachmentSourceEnum Source { get; set; }

    public required Ticket Ticket { get; set; }
}
