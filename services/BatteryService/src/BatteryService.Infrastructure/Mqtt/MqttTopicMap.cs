namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// Sprint IoT-1 (#253) — central topic builder. Đồng bộ với <c>infra/mqtt/acl.conf</c>.
/// </summary>
public static class MqttTopicMap
{
    public const string TelemetryWildcard = "telemetry/+";
    public const string HeartbeatWildcard = "heartbeat/+";
    public const string StatusWildcard = "status/+";

    public static string Telemetry(string deviceCode) => $"telemetry/{deviceCode}";
    public static string Heartbeat(string deviceCode) => $"heartbeat/{deviceCode}";
    public static string Status(string deviceCode) => $"status/{deviceCode}";
    public static string Command(string deviceCode) => $"cmd/{deviceCode}";

    /// <summary>Trích deviceCode từ topic "ns/{code}".</summary>
    public static bool TryExtractDeviceCode(string topic, out string deviceCode)
    {
        deviceCode = string.Empty;
        var slash = topic.IndexOf('/');
        if (slash < 0 || slash == topic.Length - 1)
            return false;
        deviceCode = topic[(slash + 1)..];
        return !string.IsNullOrWhiteSpace(deviceCode);
    }
}
