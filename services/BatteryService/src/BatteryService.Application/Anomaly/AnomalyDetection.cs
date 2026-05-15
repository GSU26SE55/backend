using BatteryService.Domain.Enums;

namespace BatteryService.Application.Anomaly;

/// <summary>
/// Một anomaly phát hiện được từ <see cref="AnomalyRules"/>. Pure record, không có IO.
/// </summary>
public record AnomalyDetection(
    AnomalyTypeEnum Type,
    AlertSeverityEnum Severity,
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit);
