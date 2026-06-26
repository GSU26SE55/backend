namespace NotificationService.Domain.Enums;

/// <summary>Trạng thái entry notification_audit_outbox (Sprint audit #AUDIT-34). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
