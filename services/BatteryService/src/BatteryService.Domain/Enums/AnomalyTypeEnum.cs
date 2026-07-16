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
    SensorMismatch = 15,

    /// <summary>
    /// Sprint Bonus NS-25 (#665, F1, Q11=A) — nhiệt độ dưới <c>ThresholdConfig.TemperatureMin</c>.
    /// Sạc pin lithium dưới 0°C gây lithium plating (nguy hiểm thật) — citation B2 (Feng et al.,
    /// J. Power Sources 2018). ⚠️ Wire value cross-service — đồng bộ FE + TicketService/NotificationService.
    /// </summary>
    Undertemp = 16
}
