using TicketService.Application.DTOs.Response.SLA;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string BatteryAssetId { get; set; } = string.Empty;
    public List<string> BatteryAssetIds { get; set; } = new();
    /// <summary>
    /// Customer id.
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;
    public string? AssignedStaffId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum Category { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public ImpactScopeEnum? ImpactScope { get; set; }
    /// <summary>
    /// Urgency level.
    /// </summary>
    public UrgencyLevelEnum? UrgencyLevel { get; set; }
    public TicketStatusEnum Status { get; set; }
    public TicketOriginEnum Origin { get; set; }
    /// <summary>
    /// Reopen count.
    /// </summary>
    public int ReopenCount { get; set; }
    public bool IsIncident { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Thời gian cập nhật (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    public SlaTimerDTO? SlaTimer { get; set; }
    public bool HasUnreadChat { get; set; }
}
