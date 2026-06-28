namespace SmsService.Domain.Enums;

/// <summary>Trạng thái entry sms_audit_outbox (Sprint audit #AUDIT-35). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
