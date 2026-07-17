using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Blog;

public class BlogPostActionDTO
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public BlogPostStatusEnum Status { get; set; }
    public int CurrentVersion { get; set; }
}
