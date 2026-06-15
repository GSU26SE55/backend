using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// V2 của <see cref="BatteryAnomalyDetectedEvent"/> — thêm trường cho Tier 2 monitoring
/// (Sprint 5B #105) + environmental incident scope (Sprint 5B #100).
///
/// Sprint 5B #237 — Saga subscribe cả V1 và V2 (xem overall.md §30.6).
///
/// Khác V1:
/// - <c>AssetSerialNumber</c> giờ nullable (cho EnvironmentalIncident scope site-level — không có asset).
/// - <c>SiteId</c> mới (site-level incident).
/// - <c>InternalResistanceMilliohm</c>, <c>CellVoltageDeltaMv</c> tier 2 (nullable).
/// - <c>EnvironmentalIncidentId</c> nullable (chỉ set khi anomaly type = EnvironmentalIncident).
/// </summary>
public record BatteryAnomalyDetectedV2Event(
    Guid AlertId,
    Guid? BatteryAssetId,
    Guid CustomerId,
    Guid? SiteId,
    string? AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt,
    decimal? InternalResistanceMilliohm,
    decimal? CellVoltageDeltaMv,
    Guid? EnvironmentalIncidentId
) : IntegrationEvent;
