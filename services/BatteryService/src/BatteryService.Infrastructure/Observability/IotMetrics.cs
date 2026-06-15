using Prometheus;

namespace BatteryService.Infrastructure.Observability;

/// <summary>
/// Sprint IoT-2 #IoT2-38 (S7-BE-07) — Prometheus metrics đầy đủ label theo overall.md §52.12.
/// Series mặc định được scrape bởi <c>/metrics</c> (đã enable trong Program.cs).
/// </summary>
public static class IotMetrics
{
    // Heartbeat — label status (received / late / missing).
    public static readonly Counter HeartbeatsTotal = Metrics.CreateCounter(
        "iot_device_heartbeats_total",
        "Tổng số heartbeat nhận được từ IoT device.",
        new CounterConfiguration { LabelNames = new[] { "device_id", "status" } });

    public static readonly Gauge DevicesOnline = Metrics.CreateGauge(
        "iot_devices_online_count",
        "Số IoT device đang ở trạng thái Active (snapshot, set bởi background job).");

    public static readonly Counter DevicesOfflineTotal = Metrics.CreateCounter(
        "iot_devices_offline_total",
        "Tổng số lần device chuyển sang Offline.");

    // Sensor readings.
    public static readonly Counter SensorReadingsIngestedTotal = Metrics.CreateCounter(
        "iot_sensor_readings_ingested_total",
        "Tổng số sensor reading được persist thành công.",
        new CounterConfiguration { LabelNames = new[] { "device_id" } });

    public static readonly Counter SensorReadingsRejectedTotal = Metrics.CreateCounter(
        "iot_sensor_readings_rejected_total",
        "Tổng số reading bị reject. Label reason: clock_drift, sensor_outlier, mapping_invalid, idempotency_replay, scope_denied.",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    // Firmware OTA.
    public static readonly Counter FirmwareUpdatesTotal = Metrics.CreateCounter(
        "iot_firmware_updates_total",
        "Tổng số firmware update transitions.",
        new CounterConfiguration { LabelNames = new[] { "from_version", "to_version", "status" } });

    // Auto-disable outlier (auxiliary).
    public static readonly Counter DevicesAutoDecommissionedTotal = Metrics.CreateCounter(
        "iot_devices_auto_decommissioned_total",
        "Device tự decommission do outlier vượt ngưỡng (#IoT2-17).",
        new CounterConfiguration { LabelNames = new[] { "device_id" } });

    // Cross-source mismatch (#IoT2-28).
    public static readonly Counter CrossSourceMismatchAlertsTotal = Metrics.CreateCounter(
        "iot_cross_source_mismatch_alerts_total",
        "Alert SensorMismatch sinh ra do reading BMS vs IoT lệch vượt ngưỡng.");
}
