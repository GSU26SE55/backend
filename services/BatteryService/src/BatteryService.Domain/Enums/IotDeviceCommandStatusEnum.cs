namespace BatteryService.Domain.Enums;

public enum IotDeviceCommandStatusEnum
{
    Pending = 1,
    Ok = 2,
    Failed = 3,
    Rejected = 4,
    Unknown = 5,
    TimedOut = 6
}
