namespace BatteryService.Domain.Enums;

/// <summary>Trạng thái entry battery_audit_outbox (Sprint audit #AUDIT-21). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
