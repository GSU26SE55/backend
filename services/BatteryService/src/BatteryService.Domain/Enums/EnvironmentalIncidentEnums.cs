namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint 5B #100 — loại environmental incident được report.
/// </summary>
public enum EnvironmentalIncidentTypeEnum
{
    Smoke = 1,
    FireDetected = 2,
    GasLeak = 3,
    Flood = 4,
    OverheatHazard = 5,
    Other = 99
}

/// <summary>
/// Sprint 5B #100 — lifecycle state của 1 environmental incident.
/// </summary>
public enum EnvironmentalIncidentStatusEnum
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3,
    FalseAlarm = 4
}
