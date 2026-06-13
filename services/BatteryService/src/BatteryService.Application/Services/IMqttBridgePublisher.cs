namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-1 (#253) — publish message downlink xuống MQTT broker từ Application layer.
/// Implementation trong Infrastructure (<c>MqttBridgeBackgroundService</c>) inject IMqttClient.
/// </summary>
public interface IMqttBridgePublisher
{
    /// <summary>Publish 1 message tới topic <c>cmd/{deviceCode}</c>.</summary>
    Task PublishCommandAsync(string deviceCode, string payloadJson, CancellationToken ct = default);
}
