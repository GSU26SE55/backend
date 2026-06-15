namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint IoT-2 #IoT2-38 — Application thấy interface, Infrastructure impl bằng prometheus-net counter.
/// Tách interface để handler (Application) không depend trực tiếp vào Prometheus SDK.
/// </summary>
public interface IIotMetricsRecorder
{
    void IngestRecorded(string deviceId, int count);

    /// <summary>Reason: clock_drift | sensor_outlier | mapping_invalid | idempotency_replay | scope_denied.</summary>
    void RejectionRecorded(string reason);

    void HeartbeatRecorded(string deviceId, string status);

    void DeviceOfflineRecorded();

    void DeviceAutoDecommissioned(string deviceId);

    void CrossSourceMismatchAlertRecorded();

    void FirmwareUpdateRecorded(string fromVersion, string toVersion, string status);

    void DevicesOnlineSet(double count);
}
