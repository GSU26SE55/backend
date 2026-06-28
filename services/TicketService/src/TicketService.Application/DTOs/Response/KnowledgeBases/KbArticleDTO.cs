using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    /// <summary>
    /// Tiêu đề.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    /// <summary>
    /// Solution steps.
    /// </summary>
    public string SolutionSteps { get; set; } = string.Empty;
    public List<string>? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
    /// <summary>
    /// Trạng thái.
    /// </summary>
    public KbArticleStatusEnum Status { get; set; }
    public bool IsInternalOnly { get; set; }
    public int Version { get; set; }
    /// <summary>
    /// View count.
    /// </summary>
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    /// <summary>
    /// Pending review by.
    /// </summary>
    public string? PendingReviewBy { get; set; }
    public bool ReviewRequired { get; set; }
    public string? ManagerRejectReason { get; set; }
    /// <summary>
    /// Thời gian tạo (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
