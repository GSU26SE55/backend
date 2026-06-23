using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class Ticket : AuditableEntity
{
    public required string Code { get; set; }
    public Guid BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public TicketCategoryEnum Category { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public ImpactScopeEnum? ImpactScope { get; set; }
    public UrgencyLevelEnum? UrgencyLevel { get; set; }
    public TicketStatusEnum Status { get; set; }
    public TicketOriginEnum Origin { get; set; }
    public Guid? OriginAlertId { get; set; } //link tới Alert  nếu auto
    public int ReopenCount { get; set; }
    public string? ResolutionSummary { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByManagerId { get; set; }
    public string? Reason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public short? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime? RatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public EscalationReasonEnum? EscalationReason { get; set; }
    public bool IsIncident { get; set; }

    // Navigation properties
    public SlaTimer? SlaTimer { get; set; }
    public ICollection<TicketActivity> Activities { get; set; } = new List<TicketActivity>();
    public ICollection<TicketChat> Chats { get; set; } = new List<TicketChat>();
    public ICollection<MaintenanceLog> MaintenanceLogs { get; set; } = new List<MaintenanceLog>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
    public ICollection<TicketKbReference> KbReferences { get; set; } = new List<TicketKbReference>();
    public ICollection<TicketParticipant> Participants { get; set; } = new List<TicketParticipant>();
}
