namespace BatteryService.Domain.Enums;

public enum AnomalyTypeEnum
{
    Overheat = 1,
    Overvoltage = 2,
    Undervoltage = 3,
    LowSoc = 4,
    RapidDischarge = 5,
    AbnormalCharging = 6,
    DeviceOffline = 7
}
