namespace AuthService.Domain.Enums;

/// <summary>Trạng thái 1 entry audit_outbox (Sprint audit #AUDIT-07). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
