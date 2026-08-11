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
        "Total number of heartbeats received from IoT devices.",
        new CounterConfiguration { LabelNames = new[] { "device_id", "status" } });

    public static readonly Gauge DevicesOnline = Metrics.CreateGauge(
        "iot_devices_online_count",
        "Number of IoT devices currently in the Active state (snapshot, set by a background job).");

    public static readonly Counter DevicesOfflineTotal = Metrics.CreateCounter(
        "iot_devices_offline_total",
        "Total number of times a device transitioned to Offline.");

    // Sensor readings.
    public static readonly Counter SensorReadingsIngestedTotal = Metrics.CreateCounter(
        "iot_sensor_readings_ingested_total",
        "Total number of sensor readings persisted successfully.",
        new CounterConfiguration { LabelNames = new[] { "device_id" } });

    public static readonly Counter SensorReadingsRejectedTotal = Metrics.CreateCounter(
        "iot_sensor_readings_rejected_total",
        "Total number of rejected readings. Label reason: clock_drift, sensor_outlier, mapping_invalid, idempotency_replay, scope_denied.",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    // Firmware OTA.
    public static readonly Counter FirmwareUpdatesTotal = Metrics.CreateCounter(
        "iot_firmware_updates_total",
        "Total number of firmware update transitions.",
        new CounterConfiguration { LabelNames = new[] { "from_version", "to_version", "status" } });

    // Auto-disable outlier (auxiliary).
    public static readonly Counter DevicesAutoDecommissionedTotal = Metrics.CreateCounter(
        "iot_devices_auto_decommissioned_total",
        "Devices auto-decommissioned because outliers exceeded the threshold (#IoT2-17).",
        new CounterConfiguration { LabelNames = new[] { "device_id" } });

    // Cross-source mismatch (#IoT2-28).
    public static readonly Counter CrossSourceMismatchAlertsTotal = Metrics.CreateCounter(
        "iot_cross_source_mismatch_alerts_total",
        "SensorMismatch alerts raised because the BMS and IoT readings diverged beyond the threshold.");

    // ===== MQTT bridge =====
    //
    // Alert `MqttBridgeDisconnected` (monitoring/prometheus/alert-rules.yml, nhóm iot-mqtt-bridge)
    // đã tồn tại từ trước với expr:
    //     mqtt_bridge_connected == 0 and on() mqtt_bridge_enabled == 1
    // nhưng KHÔNG file .cs nào phát ra hai series này. Vế nào cũng là vector rỗng nên phép `and`
    // cho vector rỗng ⇒ alert không bao giờ nổ, dù bridge có chết hẳn. Bổ sung ở đây để rule đó
    // thật sự hoạt động.
    //
    // Cần CẢ HAI: chỉ có `connected` thì lúc tắt bridge có chủ đích (Mqtt:Enabled=false) sẽ báo
    // động giả mãi mãi; `enabled` là điều kiện chặn đúng cho tình huống đó.
    public static readonly Gauge MqttBridgeEnabled = Metrics.CreateGauge(
        "mqtt_bridge_enabled",
        "1 = MQTT bridge is enabled (Mqtt:Enabled=true), 0 = intentionally disabled.");

    public static readonly Gauge MqttBridgeConnected = Metrics.CreateGauge(
        "mqtt_bridge_connected",
        "1 = connected to the broker, 0 = disconnected.");
}
