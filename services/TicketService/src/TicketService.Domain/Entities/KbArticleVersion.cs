using System.Text.Json;
using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class KbArticleVersion : AuditableEntity
{
    public Guid ArticleId { get; set; }
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public KbVersionStatusEnum Status { get; set; } = KbVersionStatusEnum.Pending;

    // Content snapshot
    public string Title { get; set; } = string.Empty;
    public JsonDocument Symptoms { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument DiagnosisSteps { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument SolutionSteps { get; set; } = JsonDocument.Parse("{}");
    public List<string>? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();

    // Metadata
    public string ChangeDescription { get; set; } = string.Empty;
    public Guid ChangedBy { get; set; }
    public string? ManagerRejectReason { get; set; }

    // Navigation
    public KnowledgeBaseArticle Article { get; set; } = null!;
}
