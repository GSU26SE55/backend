namespace TicketService.Application.DTOs.Response.KnowledgeBase;

public class KbArticleDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public string? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
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
