using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class BlogPostVersion : AuditableEntity
{
    public Guid BlogPostId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public Guid ChangedByUserId { get; set; }
    public string? ChangeNote { get; set; }

    // Navigation
    public BlogPost BlogPost { get; set; } = null!;
}
