namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// Sprint IoT-1 (#253) — config qua section "Mqtt" trong appsettings.
/// </summary>
public class MqttOptions
{
    public const string SectionName = "Mqtt";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8883;
    public bool UseTls { get; set; } = true;
    public bool AllowUntrustedCertificates { get; set; }
    public string Username { get; set; } = "backend-bridge";
    public string Password { get; set; } = string.Empty;
    public string ClientId { get; set; } = "battery-service-bridge";
    public int ReconnectIntervalSeconds { get; set; } = 5;
}
