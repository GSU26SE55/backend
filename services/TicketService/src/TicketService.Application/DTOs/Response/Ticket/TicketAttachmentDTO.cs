namespace TicketService.Application.DTOs.Response.Ticket;

public class TicketAttachmentDTO
{
    public string Id { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
