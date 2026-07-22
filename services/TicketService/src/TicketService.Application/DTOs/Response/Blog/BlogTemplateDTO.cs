namespace TicketService.Application.DTOs.Response.Blog;

public class BlogTemplateDTO
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
