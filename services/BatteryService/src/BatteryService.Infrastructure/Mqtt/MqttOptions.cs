namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// Sprint IoT-1 (#253) — config qua section "Mqtt" trong appsettings.
/// </summary>
public class MqttOptions
{
    public const string SectionName = "Mqtt";

    public bool Enabled { get; set; }
    /// <summary>Broker address used by BatteryService inside its deployment network.</summary>
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8883;
    /// <summary>
    /// Stable DNS/mDNS address handed to physical devices. This is deliberately
    /// separate from <see cref="Host"/> because a Docker-only name such as
    /// <c>mosquitto</c> is not resolvable by an ESP32 on the customer LAN.
    /// </summary>
    public string? PublicHost { get; set; }
    public int? PublicPort { get; set; }
    public bool UseTls { get; set; } = true;
    public bool AllowUntrustedCertificates { get; set; }
    public string Username { get; set; } = "backend-bridge";
    public string Password { get; set; } = string.Empty;
    public string ClientId { get; set; } = "battery-service-bridge";
    public int ReconnectIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Minimum period without an accepted heartbeat/telemetry signal before an MQTT LWT
    /// "offline" message may transition a device to Offline. This filters retained/stale LWT
    /// packets and short broker reconnects.
    /// </summary>
    public int LwtOfflineGraceSeconds { get; set; } = 90;

    /// <summary>
    /// GH-784 — đường dẫn file <c>passwd</c> của Mosquitto mà service ghi credential thiết bị vào.
    /// </summary>
    /// <remarks>
    /// Bỏ trống ⇒ KHÔNG đồng bộ (no-op, có log). Cố ý không mặc định một đường dẫn nào: đoán sai
    /// chỗ ghi thì hoặc là ghi vào hư không, hoặc là đè lên file của người khác.
    /// </remarks>
    public string? PasswordFilePath { get; set; }

    /// <summary>
    /// GH-784 — chu kỳ rà soát lại credential thiết bị (giây).
    /// </summary>
    /// <remarks>
    /// Mỗi lần GHI file kéo theo một lần broker nạp lại, nên vòng quét chỉ ghi khi nội dung THỰC SỰ
    /// đổi. 60 giây đủ nhanh cho việc cấp/thu hồi thiết bị mà không quấy broker.
    /// </remarks>
    public int CredentialSyncIntervalSeconds { get; set; } = 60;
}
