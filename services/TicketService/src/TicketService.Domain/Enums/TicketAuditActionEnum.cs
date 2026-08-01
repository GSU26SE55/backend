namespace TicketService.Domain.Enums;

/// <summary>
/// 28 action audit của TicketService (Sprint audit #AUDIT-24 + Sprint Chat DoD). Enum bắt đầu từ 1.
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

    // ===== Sprint Chat DoD (2026-07-31) — audit cho module Chat =====
    // DoD yêu cầu "chat.create/edit/delete/pin/unpin/reaction/mention events có causation_id trace
    // cross-service". Trước đây module Chat KHÔNG ghi audit nào — mọi thao tác trên kênh trao đổi
    // giữa Customer ↔ Staff/Manager đều không có vết forensic, trong khi đây chính là nơi dễ phát
    // sinh tranh chấp nội dung nhất (sửa/xoá tin nhắn, gỡ ghim, tag nhầm người).
    ChatCreated = 22,
    ChatEdited = 23,
    ChatDeleted = 24,
    ChatPinned = 25,
    ChatUnpinned = 26,
    ChatReacted = 27,
    ChatMentioned = 28,
}
