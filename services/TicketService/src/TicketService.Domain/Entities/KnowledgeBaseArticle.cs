using System.Text.Json;
using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class KnowledgeBaseArticle : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public JsonDocument Symptoms { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument DiagnosisSteps { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument SolutionSteps { get; set; } = JsonDocument.Parse("{}");
    public List<string>? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
    public KbArticleStatusEnum Status { get; set; } = KbArticleStatusEnum.Draft;
    public bool IsInternalOnly { get; set; } = false;
    public bool IsTemplate { get; set; } = false;
    public int Version { get; set; } = 1;
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public Guid CreatedByUserId { get; set; }

    // New fields for approval workflow
    public Guid? PendingReviewBy { get; set; }
    public bool ReviewRequired { get; set; }
    public string? ManagerRejectReason { get; set; }

    // Navigation
    public ICollection<KbArticleVersion> Versions { get; set; } = new List<KbArticleVersion>();
}
