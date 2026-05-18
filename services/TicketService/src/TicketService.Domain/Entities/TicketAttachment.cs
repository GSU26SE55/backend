using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketAttachment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid FileId { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public AttachmentSourceEnum Source { get; set; }

    public Ticket Ticket { get; set; }
}
