using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;

namespace BatteryService.Application.Anomaly;

/// <summary>
/// Pure business rules — phát hiện anomaly từ <see cref="SensorReading"/> + <see cref="ThresholdConfig"/>.
/// KHÔNG có IO, KHÔNG inject — static class. Được handler/Anomaly command gọi để detect.
///
/// Severity rules:
/// - Overheat: đạt tới TemperatureMax → Critical, đạt tới TemperatureMin → Warning
/// - Overvoltage: đạt tới VoltageMax → Critical, đạt tới VoltageMin → Warning
///   (Min/Max ở đây nghĩa là Warning/Critical — thang một chiều, KHÔNG phải dải an toàn.)
/// - LowSoc: đạt tới SocCritical → Warning, đạt tới SocWarning → Info (notification-only, xem dưới)
/// - SohDegradation: đạt tới SohCritical → Critical, đạt tới SohWarning → Warning
///
/// Mọi so sánh đều BAO GỒM mốc (&gt;= / &lt;=): số đo đúng bằng ngưỡng Admin đặt là đã vi phạm.
/// Đặt 30 nghĩa là "từ 30 trở lên báo cho tôi", không phải "trên 30 mới báo" — ngưỡng lẫn số đo
/// đều chỉ có 2 chữ số thập phân nên mốc chẵn là giá trị chạm tới được thật, không phải điểm biên
/// vô nghĩa.
/// - RapidDischarge / AbnormalCharging: Critical
/// - DeviceOffline: Warning
/// </summary>
public static class AnomalyRules
{
    public static IReadOnlyList<AnomalyDetection> Detect(SensorReading reading, ThresholdConfig threshold)
    {
        var anomalies = new List<AnomalyDetection>();

        // NHIỆT ĐỘ — hai mốc, cùng khuôn SOC/SOH: `TemperatureMin` là mốc **Warning**,
        // `TemperatureMax` là mốc **Critical**. Cả hai đều là số Admin đặt, không có hằng số nào
        // suy ra ở giữa (mốc Critical trước đây là `TemperatureMax + 5` chôn trong file này — con
        // số Admin đặt chỉ ra Warning còn mốc thật sự đẻ ticket thì không sửa được, không hiện ra).
        //
        // ⚠️ Tên cột nói "Min/Max" nhưng NGHĨA là "Warning/Critical" — đây là thang MỘT CHIỀU, không
        // phải hai đầu của một dải an toàn. Cột giữ tên cũ để khỏi phá hợp đồng gRPC sang TicketService
        // (`SensorSnapshotResponse.TemperatureMin/Max`) và mọi DTO/FE đang đọc theo tên đó.
        //
        // Hệ quả có chủ ý: KHÔNG còn rule cho phía thấp. `Undertemp` (lithium plating khi sạc dưới
        // 0°C, NS-25/#665) và `Undervoltage` (xả kiệt) không còn chỗ nào để diễn đạt ngưỡng nên đã
        // bị gỡ khỏi engine. Giá trị enum `Undertemp = 16` / `Undervoltage = 3` vẫn giữ vì alert cũ
        // trong DB tham chiếu tới chúng — chỉ là từ nay không sinh thêm.
        if (reading.Temperature >= threshold.TemperatureMax)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overheat, AlertSeverityEnum.Critical,
                threshold.TemperatureMax, reading.Temperature, "°C"));
        }
        else if (reading.Temperature >= threshold.TemperatureMin)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overheat, AlertSeverityEnum.Warning,
                threshold.TemperatureMin, reading.Temperature, "°C"));
        }

        // ĐIỆN ÁP — cùng quy ước: `VoltageMin` là Warning, `VoltageMax` là Critical.
        // `else if` chứ không phải hai `if` rời: vượt mốc Critical thì CHỈ ra một alert Critical,
        // nếu không mỗi số đo quá ngưỡng sẽ đẻ hai alert chồng nhau cho cùng một sự cố.
        if (reading.Voltage >= threshold.VoltageMax)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overvoltage, AlertSeverityEnum.Critical,
                threshold.VoltageMax, reading.Voltage, "V"));
        }
        else if (reading.Voltage >= threshold.VoltageMin)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.Overvoltage, AlertSeverityEnum.Warning,
                threshold.VoltageMin, reading.Voltage, "V"));
        }

        // SOC — notification-only, KHÔNG bao giờ chạm mức Critical.
        //
        // Pin trong hệ solar xả mỗi đêm; chạm ngưỡng SOC là kết quả tất yếu của tải và chu kỳ
        // nắng, không phải hỏng hóc. Ở mức Critical thì `AnomalyDetectionService` publish
        // `BatteryAnomalyDetectedEvent` + V2 ⇒ Saga ⇒ ticket auto — mỗi tối một ticket rác cho
        // mọi pin dùng cạn. Hạ một bậc (Critical→Warning, Warning→Info) đưa LowSoc sang nhánh
        // `BatteryAnomalyWarningDetectedEvent`, event mà CHỈ NotificationService consume: alert
        // vẫn ghi, khách vẫn được báo "pin yếu", chỉ không có ticket.
        //
        // ⚠️ Đây là chỗ DUY NHẤT quyết định việc đó. Nâng lại Critical là ticket rác quay lại.
        // Ngưỡng số (`SocWarningThreshold` 20% / `SocCriticalThreshold` 10%) KHÔNG đổi — chỉ đổi
        // mức nghiêm trọng mà chúng sinh ra. Hệ quả kênh gửi: ≤10% → InApp+Push, ≤20% → InApp.
        //
        // Nửa AI của cùng triệu chứng đã hạ song song: `VOLTAGE_LOW` warning→info và `VerifyTicket`
        // thôi cộng điểm khi SOC dưới ngưỡng (ai-module, docs/be-huong-dan-tich-hop.md §10.3).
        //
        // Cái MẤT: không còn đường tự bắt pin xả kiệt hư cell thật. Ca đó cần lịch sử nhiều giờ
        // ("không hồi phục sau trọn một cửa sổ sạc" / "dưới ngưỡng bảo vệ deep-discharge") mà
        // `ThresholdConfig` chưa có cột nào diễn đạt được — `VoltageMin` là mốc Warning của thang
        // QUÁ ÁP, không phải sàn điện áp, nên không dùng lại được. Việc đó tách riêng.
        // Trước mắt `RapidDischarge` / `SohDegradation` / `CellImbalance` /
        // `HighInternalResistance` vẫn Critical và vẫn đẻ ticket.
        if (reading.SocPercent <= threshold.SocCriticalThreshold)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Warning,
                threshold.SocCriticalThreshold, reading.SocPercent, "%"));
        }
        else if (reading.SocPercent <= threshold.SocWarningThreshold)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.LowSoc, AlertSeverityEnum.Info,
                threshold.SocWarningThreshold, reading.SocPercent, "%"));
        }

        if (threshold.CurrentMaxDischarge.HasValue && reading.Current < 0
            && Math.Abs(reading.Current) >= threshold.CurrentMaxDischarge.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.RapidDischarge, AlertSeverityEnum.Critical,
                threshold.CurrentMaxDischarge.Value, Math.Abs(reading.Current), "A"));
        }

        if (threshold.CurrentMaxCharge.HasValue && reading.Current >= threshold.CurrentMaxCharge.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.AbnormalCharging, AlertSeverityEnum.Critical,
                threshold.CurrentMaxCharge.Value, reading.Current, "A"));
        }

        if (reading.SohPercent.HasValue)
        {
            if (threshold.SohCriticalThreshold.HasValue
                && reading.SohPercent.Value <= threshold.SohCriticalThreshold.Value)
            {
                anomalies.Add(new AnomalyDetection(
                    AnomalyTypeEnum.SohDegradation, AlertSeverityEnum.Critical,
                    threshold.SohCriticalThreshold.Value, reading.SohPercent.Value, "%"));
            }
            else if (threshold.SohWarningThreshold.HasValue
                     && reading.SohPercent.Value <= threshold.SohWarningThreshold.Value)
            {
                anomalies.Add(new AnomalyDetection(
                    AnomalyTypeEnum.SohDegradation, AlertSeverityEnum.Warning,
                    threshold.SohWarningThreshold.Value, reading.SohPercent.Value, "%"));
            }
        }

        // Sprint 5B #105 — Tier 2 anomalies.
        if (reading.InternalResistanceMilliohm.HasValue && threshold.InternalResistanceMaxMilliohm.HasValue
            && reading.InternalResistanceMilliohm.Value >= threshold.InternalResistanceMaxMilliohm.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighInternalResistance, AlertSeverityEnum.Critical,
                threshold.InternalResistanceMaxMilliohm.Value,
                reading.InternalResistanceMilliohm.Value, "mΩ"));
        }

        if (reading.CellVoltageDeltaMv.HasValue && threshold.CellVoltageDeltaMaxMv.HasValue
            && reading.CellVoltageDeltaMv.Value >= threshold.CellVoltageDeltaMaxMv.Value)
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
                reading.AmbientTemperature ?? 0m, "°C"));
        }
        else if (threshold.HighAmbientTempWarning.HasValue
                 && reading.AmbientTemperature > threshold.HighAmbientTempWarning.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighAmbientTemp, AlertSeverityEnum.Warning,
                threshold.HighAmbientTempWarning.Value,
                reading.AmbientTemperature ?? 0m, "°C"));
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

        if (threshold.HighGasCritical.HasValue
            && reading.GasConcentration > threshold.HighGasCritical.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighGasConcentration, AlertSeverityEnum.Critical,
                threshold.HighGasCritical.Value,
                reading.GasConcentration ?? 0m, "%"));
        }
        else if (threshold.HighGasWarning.HasValue
                 && reading.GasConcentration > threshold.HighGasWarning.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighGasConcentration, AlertSeverityEnum.Warning,
                threshold.HighGasWarning.Value,
                reading.GasConcentration ?? 0m, "%"));
        }

        // Combo rule — cả 2 cùng vượt threshold → Critical riêng.
        if (threshold.ComboTempThreshold.HasValue && threshold.ComboHumidityThreshold.HasValue
            && reading.AmbientTemperature >= threshold.ComboTempThreshold.Value
            && reading.Humidity >= threshold.ComboHumidityThreshold.Value)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.HighTempHumidityCombo, AlertSeverityEnum.Critical,
                threshold.ComboTempThreshold.Value,
                reading.AmbientTemperature ?? 0m, "°C"));
        }

        // Water leak — cảm biến báo ướt/khô, không có ngưỡng số nên luôn Critical khi true.
        if (reading.WaterLeakDetected == true)
        {
            anomalies.Add(new AnomalyDetection(
                AnomalyTypeEnum.WaterLeak, AlertSeverityEnum.Critical,
                1m, 1m, "Wet"));
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

        // DS18B20 chỉ đo nhiệt; voltage=0 trong payload là placeholder, không phải số đo.
        var voltageDelta = Math.Abs(bms.Voltage - iot.Voltage);
        if (MeasuresVoltage(bms) && MeasuresVoltage(iot)
            && voltageDelta > SensorMismatchVoltageDeltaV)
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

    public static bool MeasuresVoltage(SensorReading reading) =>
        !string.Equals(reading.SensorSourceCode, "external-temp", StringComparison.OrdinalIgnoreCase);
}
