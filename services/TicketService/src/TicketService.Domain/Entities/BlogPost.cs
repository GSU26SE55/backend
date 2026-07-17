using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class BlogPost : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public BlogPostStatusEnum Status { get; set; } = BlogPostStatusEnum.Draft;
    public BlogPostOriginEnum Origin { get; set; } = BlogPostOriginEnum.Manual;

    // Linking to KB article when AI-generated
    public Guid? SourceKbArticleId { get; set; }

    // Template used when manually created
    public Guid? BlogTemplateId { get; set; }

    public Guid AuthorUserId { get; set; }
    public int CurrentVersion { get; set; } = 1;

    // Navigation
    public KnowledgeBaseArticle? SourceKbArticle { get; set; }
    public BlogTemplate? BlogTemplate { get; set; }
    public ICollection<BlogPostVersion> Versions { get; set; } = new List<BlogPostVersion>();
}
