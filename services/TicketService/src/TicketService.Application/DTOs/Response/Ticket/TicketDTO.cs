using TicketService.Application.DTOs.Response.SLA;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Ticket;

public class TicketDTO
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string BatteryAssetId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? AssignedStaffId { get; set; }
    public string Title { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public ImpactScopeEnum? ImpactScope { get; set; }
    public UrgencyLevelEnum? UrgencyLevel { get; set; }
    public TicketStatusEnum Status { get; set; }
    public TicketOriginEnum Origin { get; set; }
    public int ReopenCount { get; set; }
    public bool IsIncident { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public SlaTimerDTO? SlaTimer { get; set; }
}
