namespace TicketService.Domain.Enums;

/// <summary>Trạng thái entry ticket_audit_outbox (Sprint audit #AUDIT-25). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
