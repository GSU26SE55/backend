using BatteryService.Application.Interfaces;

namespace BatteryService.Infrastructure.Observability;

/// <summary>
/// Sprint IoT-2 #IoT2-38 — bridge từ Application sang prometheus-net counters.
/// </summary>
internal sealed class IotMetricsRecorder : IIotMetricsRecorder
{
    public void IngestRecorded(string deviceId, int count)
    {
        if (count > 0)
            IotMetrics.SensorReadingsIngestedTotal.WithLabels(deviceId).Inc(count);
    }

    public void RejectionRecorded(string reason)
    {
        IotMetrics.SensorReadingsRejectedTotal.WithLabels(reason).Inc();
    }

    public void HeartbeatRecorded(string deviceId, string status)
    {
        IotMetrics.HeartbeatsTotal.WithLabels(deviceId, status).Inc();
    }

    public void DeviceOfflineRecorded()
    {
        IotMetrics.DevicesOfflineTotal.Inc();
    }

    public void DeviceAutoDecommissioned(string deviceId)
    {
        IotMetrics.DevicesAutoDecommissionedTotal.WithLabels(deviceId).Inc();
    }

    public void CrossSourceMismatchAlertRecorded()
    {
        IotMetrics.CrossSourceMismatchAlertsTotal.Inc();
    }

    public void FirmwareUpdateRecorded(string fromVersion, string toVersion, string status)
    {
        IotMetrics.FirmwareUpdatesTotal.WithLabels(fromVersion, toVersion, status).Inc();
    }

    public void DevicesOnlineSet(double count)
    {
        IotMetrics.DevicesOnline.Set(count);
    }
}
