using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketAttachment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid? SourceTicketId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid FileId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public AttachmentSourceEnum Source { get; set; }
    public Guid? ChatId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
    public bool IsInline { get; set; }
    public int DownloadCount { get; set; }
    public VirusScanStatusEnum VirusScanStatus { get; set; } = VirusScanStatusEnum.Pending;

    public required Ticket Ticket { get; set; }
    public TicketChat? Chat { get; set; }
}
