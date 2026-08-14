using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// GH-784 — test double cho <see cref="IMqttBrokerEndpointProvider"/>.
/// </summary>
/// <remarks>
/// Mặc định trả một broker ĐANG BẬT: các test có trước GH-784 kiểm hành vi nghiệp vụ chứ không
/// kiểm nhánh "MQTT tắt", nên phải chạy ở cấu hình bình thường để assertion cũ giữ nguyên ý nghĩa.
/// </remarks>
public sealed class TestMqttBrokerEndpointProvider : IMqttBrokerEndpointProvider
{
    private readonly MqttBrokerEndpoint _endpoint;

    private TestMqttBrokerEndpointProvider(MqttBrokerEndpoint endpoint) => _endpoint = endpoint;

    public static TestMqttBrokerEndpointProvider Enabled(
        string host = "mqtt.local", int port = 8883, bool useTls = true)
        => new(new MqttBrokerEndpoint(host, port, useTls, TopicPrefix: null));

    /// <summary>MQTT tắt ⇒ không có endpoint nào để đưa cho thiết bị.</summary>
    public static TestMqttBrokerEndpointProvider Disabled() => new(MqttBrokerEndpoint.Disabled);

    public MqttBrokerEndpoint Resolve(string deviceCode)
        => _endpoint.Host is null
            ? MqttBrokerEndpoint.Disabled
            : _endpoint with { TopicPrefix = $"solar/{deviceCode.Trim().ToLowerInvariant()}" };
}
