using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleTemplateDTO
{
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum Category { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}
