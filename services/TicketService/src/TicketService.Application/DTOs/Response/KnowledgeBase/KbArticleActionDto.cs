using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBase;

public class KbArticleActionDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public KbArticleStatusEnum Status { get; set; }
}
