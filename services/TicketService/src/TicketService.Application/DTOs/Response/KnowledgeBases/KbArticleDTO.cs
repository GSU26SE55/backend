using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleDTO
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public List<string>? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
    public KbArticleStatusEnum Status { get; set; }
    public bool IsInternalOnly { get; set; }
    public int Version { get; set; }
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? PendingReviewBy { get; set; }
    public bool ReviewRequired { get; set; }
    public string? ManagerRejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
