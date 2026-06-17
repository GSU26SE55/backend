namespace TicketService.Application.DTOs.Response.KnowledgeBase;

public class KbArticleSuggestDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public int ViewCount { get; set; }
}
