namespace FileStorageService.Domain.Enums;

/// <summary>Trạng thái entry file_audit_outbox (Sprint audit #AUDIT-29). Enum bắt đầu từ 1.</summary>
public enum AuditOutboxStatusEnum
{
    Pending = 1,
    Published = 2,
    Failed = 3,
}
