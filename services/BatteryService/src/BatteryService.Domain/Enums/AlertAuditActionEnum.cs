namespace BatteryService.Domain.Enums;

/// <summary>
/// 5 action audit của Alert (Sprint audit #AUDIT-31). Host trong BatteryService (D14 — không tách Alert service riêng).
/// Enum bắt đầu từ 1.
/// </summary>
public enum AlertAuditActionEnum
{
    AlertAcknowledged = 1,
    AlertSuppressed = 2,
    AlertRuleChanged = 3,
    AlertSeverityOverridden = 4,
    AlertManuallyResolved = 5,
}
