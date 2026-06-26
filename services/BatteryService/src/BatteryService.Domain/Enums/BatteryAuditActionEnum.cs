namespace BatteryService.Domain.Enums;

/// <summary>12 action audit của BatteryService (Sprint audit #AUDIT-20). Enum bắt đầu từ 1.</summary>
public enum BatteryAuditActionEnum
{
    BatteryCreated = 1,
    BatteryUpdated = 2,
    BatteryDeleted = 3,
    AssignedToCustomer = 4,
    UnassignedFromCustomer = 5,
    ThresholdConfigChanged = 6,
    SensorReadingEdited = 7,
    AlertAcknowledged = 8,
    AlertSuppressed = 9,
    StatusChanged = 10,
    MaintenanceLogged = 11,
    CalibrationApplied = 12,
}
