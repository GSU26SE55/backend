using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketAttachmentDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string? ChatId { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public AttachmentSourceEnum Source { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsInline { get; set; }
    public int DownloadCount { get; set; }
    public VirusScanStatusEnum VirusScanStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}
