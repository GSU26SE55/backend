using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.TicketKbReferences;

public class TicketKbReferenceDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string KbArticleId { get; set; } = string.Empty;
    /// <summary>
    /// Kb article code.
    /// </summary>
    public string KbArticleCode { get; set; } = string.Empty;
    public string? KbArticleTitle { get; set; }
    public string ReferencedByUserId { get; set; } = string.Empty;
    /// <summary>
    /// Reference type.
    /// </summary>
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
