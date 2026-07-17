using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Blog;

public class BlogPostDTO
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public BlogPostStatusEnum Status { get; set; }
    public BlogPostOriginEnum Origin { get; set; }
    public string? SourceKbArticleId { get; set; }
    public string? BlogTemplateId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public int CurrentVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
