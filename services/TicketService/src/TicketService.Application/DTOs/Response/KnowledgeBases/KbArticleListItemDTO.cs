using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleListItemDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum Category { get; set; }
    public KbArticleStatusEnum Status { get; set; }
    public int ViewCount { get; set; }
    /// <summary>
    /// Helpful count.
    /// </summary>
    public int HelpfulCount { get; set; }
    public bool ReviewRequired { get; set; }
    public DateTime CreatedAt { get; set; }
}
