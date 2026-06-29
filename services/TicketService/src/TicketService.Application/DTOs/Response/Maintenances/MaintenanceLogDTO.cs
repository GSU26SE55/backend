using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Maintenances;

public class MaintenanceLogDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public MaintenanceLogTypeEnum LogType { get; set; }
    /// <summary>
    /// Summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    /// <summary>
    /// Duration minutes.
    /// </summary>
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>
    /// Completed at.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    public List<string> AttachmentFileIds { get; set; } = new();
    public List<string> BeforePhotosFileIds { get; set; } = new();
    /// <summary>
    /// After photos file ids.
    /// </summary>
    public List<string> AfterPhotosFileIds { get; set; } = new();
    public List<string> RelatedKbArticleIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
