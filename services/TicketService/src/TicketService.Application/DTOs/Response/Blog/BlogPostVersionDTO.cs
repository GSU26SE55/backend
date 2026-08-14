namespace TicketService.Application.DTOs.Response.Blog;

public class BlogPostVersionDTO
{
    public string Id { get; set; } = string.Empty;
    public string BlogPostId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
}
