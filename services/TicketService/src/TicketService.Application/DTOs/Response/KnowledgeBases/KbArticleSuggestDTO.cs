namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleSuggestDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public int ViewCount { get; set; }
}
