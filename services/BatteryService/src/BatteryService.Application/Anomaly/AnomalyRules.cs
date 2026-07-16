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

        // Sprint 5B #105 — Tier 2 anomalies.
        if (reading.InternalResistanceMilliohm.HasValue && threshold.InternalResistanceMaxMilliohm.HasValue
            && reading.InternalResistanceMilliohm.Value > threshold.InternalResistanceMaxMilliohm.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighInternalResistance, AlertSeverityEnum.Critical,
                threshold.InternalResistanceMaxMilliohm.Value,
                reading.InternalResistanceMilliohm.Value, "mΩ"));
        }

        if (reading.CellVoltageDeltaMv.HasValue && threshold.CellVoltageDeltaMaxMv.HasValue
            && reading.CellVoltageDeltaMv.Value > threshold.CellVoltageDeltaMaxMv.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.CellImbalance, AlertSeverityEnum.Critical,
                threshold.CellVoltageDeltaMaxMv.Value,
                reading.CellVoltageDeltaMv.Value, "mV"));
        }

        return anomalies;
    }

    /// <summary>
    /// Sprint 5B #93 — Ambient anomaly detection từ <see cref="AmbientReading"/>.
    /// </summary>
    public static IReadOnlyList<AnomalyDetection> DetectAmbient(
        AmbientReading reading,
        AmbientThresholdConfig threshold)
    {
        var anomalies = new List<AnomalyDetection>();

        if (!threshold.Enabled)
            return anomalies;

        if (threshold.HighAmbientTempCritical.HasValue
            && reading.AmbientTemperature > threshold.HighAmbientTempCritical.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighAmbientTemp, AlertSeverityEnum.Critical,
                threshold.HighAmbientTempCritical.Value,
                reading.AmbientTemperature, "°C"));
        }
        else if (threshold.HighAmbientTempWarning.HasValue
                 && reading.AmbientTemperature > threshold.HighAmbientTempWarning.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighAmbientTemp, AlertSeverityEnum.Warning,
                threshold.HighAmbientTempWarning.Value,
                reading.AmbientTemperature, "°C"));
        }

        if (threshold.HighHumidityCritical.HasValue
            && reading.Humidity > threshold.HighHumidityCritical.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighHumidity, AlertSeverityEnum.Critical,
                threshold.HighHumidityCritical.Value,
                reading.Humidity ?? 0m, "%"));
        }
        else if (threshold.HighHumidityWarning.HasValue
                 && reading.Humidity > threshold.HighHumidityWarning.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighHumidity, AlertSeverityEnum.Warning,
                threshold.HighHumidityWarning.Value,
                reading.Humidity ?? 0m, "%"));
        }

        // Combo rule — cả 2 cùng vượt threshold → Critical riêng.
        if (threshold.ComboTempThreshold.HasValue && threshold.ComboHumidityThreshold.HasValue
            && reading.AmbientTemperature >= threshold.ComboTempThreshold.Value
            && reading.Humidity >= threshold.ComboHumidityThreshold.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighTempHumidityCombo, AlertSeverityEnum.Critical,
                threshold.ComboTempThreshold.Value,
                reading.AmbientTemperature, "°C"));
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

    /// <summary>
    /// Sprint 5B B10 (#157) — Cross-source validation: so sánh BMS reading vs IoT Gateway reading
    /// trên cùng asset trong cùng cửa sổ 60s.
    /// - |Voltage_bms − Voltage_iot| > 0.5V → SensorMismatch Warning.
    /// - |Temperature_bms − Temperature_iot| > 5°C → SensorMismatch Warning.
    /// </summary>
    public const decimal SensorMismatchVoltageDeltaV = 0.5m;
    public const decimal SensorMismatchTemperatureDeltaC = 5m;

    public static AnomalyDetection? DetectSensorMismatch(SensorReading bms, SensorReading iot)
    {
        if (bms.SourceType != SensorReadingSourceTypeEnum.Bms
            || iot.SourceType != SensorReadingSourceTypeEnum.IotGateway)
            return null;

        var voltageDelta = Math.Abs(bms.Voltage - iot.Voltage);
        if (voltageDelta > SensorMismatchVoltageDeltaV)
        {
            return new AnomalyDetection(
                AnomalyTypeEnum.SensorMismatch, AlertSeverityEnum.Warning,
                SensorMismatchVoltageDeltaV, voltageDelta, "V");
        }

        // Sprint Bonus NS-09 (#653, N5) — nguồn `redundant` (INA226) KHÔNG đo nhiệt độ:
        // firmware set cứng temp=0 và kỳ vọng backend skip so sánh nhiệt (contract firmware
        // ina226.cpp). Không skip → BMS 25°C vs 0°C = mismatch giả liên tục trên mọi pin.
        if (MeasuresTemperature(bms) && MeasuresTemperature(iot))
        {
            var tempDelta = Math.Abs(bms.Temperature - iot.Temperature);
            if (tempDelta > SensorMismatchTemperatureDeltaC)
            {
                return new AnomalyDetection(
                    AnomalyTypeEnum.SensorMismatch, AlertSeverityEnum.Warning,
                    SensorMismatchTemperatureDeltaC, tempDelta, "°C");
            }
        }

        return null;
    }

    /// <summary>
    /// Sprint Bonus NS-09 (#653, N5) — nguồn <c>redundant</c> (INA226, shunt dòng) không có
    /// cảm biến nhiệt → temp trong payload là 0 cứng, không được dùng để so sánh cross-source.
    /// </summary>
    public static bool MeasuresTemperature(SensorReading reading) =>
        !string.Equals(reading.SensorSourceCode, "redundant", StringComparison.OrdinalIgnoreCase);
}
