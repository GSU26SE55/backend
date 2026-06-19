using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleListItemDTO
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public KbArticleStatusEnum Status { get; set; }
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public bool ReviewRequired { get; set; }
    public DateTime CreatedAt { get; set; }
}
