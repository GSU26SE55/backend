namespace TicketService.Application.DTOs.Response.KnowledgeBase;

public class KbArticleListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public bool ReviewRequired { get; set; }
    public DateTime CreatedAt { get; set; }
}
