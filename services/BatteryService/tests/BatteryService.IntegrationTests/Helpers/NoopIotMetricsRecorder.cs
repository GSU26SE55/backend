using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

public sealed class NoopIotMetricsRecorder : IIotMetricsRecorder
{
    public void IngestRecorded(string deviceId, int count) { }
    public void RejectionRecorded(string reason) { }
    public void HeartbeatRecorded(string deviceId, string status) { }
    public void DeviceOfflineRecorded() { }
    public void DeviceAutoDecommissioned(string deviceId) { }
    public void CrossSourceMismatchAlertRecorded() { }
    public void FirmwareUpdateRecorded(string fromVersion, string toVersion, string status) { }
    public void DevicesOnlineSet(double count) { }
}
