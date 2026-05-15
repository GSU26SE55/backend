using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;

namespace BatteryService.Application.Anomaly;

/// <summary>
/// Pure business rules — phát hiện anomaly từ <see cref="SensorReading"/> + <see cref="ThresholdConfig"/>.
/// KHÔNG có IO, KHÔNG inject — static class. Được handler/Anomaly command gọi để detect.
///
/// Severity rules:
/// - Overheat: vượt &gt; 5°C ngưỡng → Critical, ngược lại Warning
/// - Overvoltage / Undervoltage: Critical (an toàn)
/// - LowSoc: dưới SocCritical → Critical, dưới SocWarning → Warning
/// - SohDegradation: dưới SohCritical → Critical, dưới SohWarning → Warning
/// - RapidDischarge / AbnormalCharging: Critical
/// - DeviceOffline: Warning
/// </summary>
public static class AnomalyRules
{
    private const decimal OverheatCriticalDeltaC = 5m;

    public static IReadOnlyList<AnomalyDetection> Detect(SensorReading reading, ThresholdConfig threshold)
    {
        var anomalies = new List<AnomalyDetection>();

        if (reading.Temperature > threshold.TemperatureMax)
        {
            var severity = reading.Temperature > threshold.TemperatureMax + OverheatCriticalDeltaC
                ? AlertSeverityEnum.Critical
                : AlertSeverityEnum.Warning;
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overheat, severity,
                threshold.TemperatureMax, reading.Temperature, "°C"));
        }

        if (reading.Voltage > threshold.VoltageMax)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overvoltage, AlertSeverityEnum.Critical,
                threshold.VoltageMax, reading.Voltage, "V"));
        }

        if (reading.Voltage < threshold.VoltageMin)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Undervoltage, AlertSeverityEnum.Critical,
                threshold.VoltageMin, reading.Voltage, "V"));
        }

        if (reading.SocPercent < threshold.SocCriticalThreshold)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Critical,
                threshold.SocCriticalThreshold, reading.SocPercent, "%"));
        }
        else if (reading.SocPercent < threshold.SocWarningThreshold)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Warning,
                threshold.SocWarningThreshold, reading.SocPercent, "%"));
        }

        if (threshold.CurrentMaxDischarge.HasValue && reading.Current < 0
            && Math.Abs(reading.Current) > threshold.CurrentMaxDischarge.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.RapidDischarge, AlertSeverityEnum.Critical,
                threshold.CurrentMaxDischarge.Value, Math.Abs(reading.Current), "A"));
        }

        if (threshold.CurrentMaxCharge.HasValue && reading.Current > threshold.CurrentMaxCharge.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.AbnormalCharging, AlertSeverityEnum.Critical,
                threshold.CurrentMaxCharge.Value, reading.Current, "A"));
        }

        if (reading.SohPercent.HasValue)
        {
            if (threshold.SohCriticalThreshold.HasValue
                && reading.SohPercent.Value < threshold.SohCriticalThreshold.Value)
            {
                anomalies.Add(new AnomalyDetection(
                    AnomalyTypeEnum.SohDegradation, AlertSeverityEnum.Critical,
                    threshold.SohCriticalThreshold.Value, reading.SohPercent.Value, "%"));
            }
            else if (threshold.SohWarningThreshold.HasValue
                     && reading.SohPercent.Value < threshold.SohWarningThreshold.Value)
            {
                anomalies.Add(new AnomalyDetection(
                    AnomalyTypeEnum.SohDegradation, AlertSeverityEnum.Warning,
                    threshold.SohWarningThreshold.Value, reading.SohPercent.Value, "%"));
            }
        }

        return anomalies;
    }

    public static AnomalyDetection? DetectOffline(BatteryAsset asset, TimeSpan offlineThreshold, DateTime now)
    {
        if (!asset.LastSensorReadingAt.HasValue)
            return null;
        var elapsed = now - asset.LastSensorReadingAt.Value;
        if (elapsed <= offlineThreshold)
            return null;

        return new AnomalyDetection(
            AnomalyTypeEnum.DeviceOffline, AlertSeverityEnum.Warning,
            (decimal)offlineThreshold.TotalMinutes, (decimal)elapsed.TotalMinutes, "min");
    }
}
