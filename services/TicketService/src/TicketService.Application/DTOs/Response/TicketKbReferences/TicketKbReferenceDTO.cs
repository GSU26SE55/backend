using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.TicketKbReferences;

public class TicketKbReferenceDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string KbArticleId { get; set; } = string.Empty;
    public string KbArticleCode { get; set; } = string.Empty;
    public string? KbArticleTitle { get; set; }
    public string ReferencedByUserId { get; set; } = string.Empty;
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
