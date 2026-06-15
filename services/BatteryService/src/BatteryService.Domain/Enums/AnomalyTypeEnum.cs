namespace BatteryService.Domain.Enums;

public enum AnomalyTypeEnum
{
    Overheat = 1,
    Overvoltage = 2,
    Undervoltage = 3,
    LowSoc = 4,
    RapidDischarge = 5,
    AbnormalCharging = 6,
    DeviceOffline = 7,
    SohDegradation = 8,        // SOH dưới ngưỡng — pin xuống cấp tiến đến EOL

    // Sprint 5B #93 — Ambient anomaly types.
    HighAmbientTemp = 9,
    HighHumidity = 10,
    HighTempHumidityCombo = 11,

    // Sprint 5B #105 — Tier 2 battery health.
    HighInternalResistance = 12,
    CellImbalance = 13,

    // Sprint 5B #104/#105 — Environmental incident scope (site-level).
    EnvironmentalIncident = 14,

    // Sprint 7 #157 (B10) — Cross-source mismatch BMS vs IoT.
    SensorMismatch = 15
}
