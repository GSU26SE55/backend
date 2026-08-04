namespace TicketService.Application.DTOs.Response.Blog;

public class BlogDiffDTO
{
    public int OldVersionNumber { get; set; }
    public int NewVersionNumber { get; set; }
    public string OldContentHtml { get; set; } = string.Empty;
    public string NewContentHtml { get; set; } = string.Empty;
}
