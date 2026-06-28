namespace TicketService.Domain.Enums;

/// <summary>
/// 21 action audit của TicketService (Sprint audit #AUDIT-24). Enum bắt đầu từ 1.
/// TÁCH khỏi TicketActivity (UI timeline user-facing) — 2 entity khác purpose.
/// </summary>
public enum TicketAuditActionEnum
{
    TicketCreated = 1,
    StateTransitioned = 2,
    PriorityChanged = 3,
    AssignedToStaff = 4,
    UnassignedFromStaff = 5,
    SlaPaused = 6,
    SlaResumed = 7,
    SlaBreached = 8,
    EscalatedToManager = 9,
    EscalatedToAdmin = 10,
    MaintenanceLogAdded = 11,
    CommentAdded = 12,
    AttachmentUploaded = 13,
    AttachmentDeleted = 14,
    ResolutionAdded = 15,
    ClosedByUser = 16,
    ReopenedByAdmin = 17,
    RejectedByManager = 18,
    FalseAlarmMarked = 19,
    CustomerRated = 20,
    AutoCreatedFromAnomaly = 21,
}
