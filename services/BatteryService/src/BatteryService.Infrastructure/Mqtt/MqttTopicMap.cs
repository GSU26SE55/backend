namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// Sprint IoT-2 #IoT2-22 (S4-BE-02) — topic schema mới theo overall.md §52.14.
/// Subscribe-side 4 wildcard, publish-side 1 (cmd downlink).
///
/// Schema:
/// <list type="bullet">
///   <item><c>solar/{deviceCode}/{batteryAssetSerial}/telemetry</c> — sensor readings từ device cho 1 pin cụ thể.</item>
///   <item><c>solar/{deviceCode}/heartbeat</c> — health/heartbeat từ device.</item>
///   <item><c>solar/{deviceCode}/status</c> — LWT payload "online" / "offline".</item>
///   <item><c>solar/{deviceCode}/cmd</c> — downlink command từ backend.</item>
///   <item><c>solar/{deviceCode}/cmd/ack</c> — ack của device sau khi execute cmd.</item>
/// </list>
/// </summary>
public static class MqttTopicMap
{
    public const string TopicPrefix = "solar";

    public const string TelemetryWildcard = "solar/+/+/telemetry";
    public const string HeartbeatWildcard = "solar/+/heartbeat";
    public const string StatusWildcard = "solar/+/status";
    public const string CommandAckWildcard = "solar/+/cmd/ack";

    public static string Telemetry(string deviceCode, string batterySerial) => $"solar/{deviceCode}/{batterySerial}/telemetry";

    public static string Heartbeat(string deviceCode) => $"solar/{deviceCode}/heartbeat";

    public static string Status(string deviceCode) => $"solar/{deviceCode}/status";

    public static string Command(string deviceCode) => $"solar/{deviceCode}/cmd";

    public static string CommandAck(string deviceCode) => $"solar/{deviceCode}/cmd/ack";

    /// <summary>
    /// Parse topic, trả về deviceCode + classify segment cuối.
    /// </summary>
    /// <param name="topic">Raw topic incoming.</param>
    /// <param name="deviceCode">Output deviceCode hoặc empty.</param>
    /// <param name="kind">Loại topic: telemetry | heartbeat | status | cmd_ack.</param>
    /// <param name="batterySerial">Chỉ set khi <paramref name="kind"/> = "telemetry".</param>
    /// <returns>True nếu topic hợp lệ và bắt đầu bằng <c>solar/</c>.</returns>
    public static bool TryParse(string topic, out string deviceCode, out string kind, out string? batterySerial)
    {
        deviceCode = string.Empty;
        kind = string.Empty;
        batterySerial = null;

        if (string.IsNullOrWhiteSpace(topic))
            return false;

        var parts = topic.Split('/');
        if (parts.Length < 3 || parts[0] != TopicPrefix)
            return false;

        deviceCode = parts[1];
        if (string.IsNullOrWhiteSpace(deviceCode))
            return false;

        if (parts.Length == 3)
        {
            kind = parts[2];
            return kind is "heartbeat" or "status" or "cmd";
        }

        if (parts.Length == 4 && parts[3] == "telemetry")
        {
            kind = "telemetry";
            batterySerial = parts[2];
            return !string.IsNullOrWhiteSpace(batterySerial);
        }

        if (parts.Length == 4 && parts[2] == "cmd" && parts[3] == "ack")
        {
            kind = "cmd_ack";
            return true;
        }

        return false;
    }
}
