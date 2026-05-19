using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class KnowledgeBaseArticle : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public string? RecommendedParts { get; set; } // JSON
    public List<string> Tags { get; set; } = new();
    public KbArticleStatusEnum Status { get; set; } = KbArticleStatusEnum.Draft;
    public int Version { get; set; } = 1;
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public Guid CreatedByUserId { get; set; }
}
