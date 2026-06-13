using BatteryService.Infrastructure.Mqtt;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint IoT-1 #253 — verify topic builder + reverse extraction.
/// Wildcard pattern phải khớp ACL trong infra/mqtt/acl.conf.
/// </summary>
public class MqttTopicMapTests
{
    [Theory]
    [InlineData("telemetry/+")]
    [InlineData("heartbeat/+")]
    [InlineData("status/+")]
    public void Wildcards_FollowSparkplugStylePerDevice(string expected)
    {
        var actual = expected switch
        {
            "telemetry/+" => MqttTopicMap.TelemetryWildcard,
            "heartbeat/+" => MqttTopicMap.HeartbeatWildcard,
            "status/+" => MqttTopicMap.StatusWildcard,
            _ => string.Empty
        };
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("ESP32-001", "telemetry/ESP32-001")]
    [InlineData("device-with-dash", "telemetry/device-with-dash")]
    public void Telemetry_BuildsPerDeviceTopic(string deviceCode, string expected)
        => MqttTopicMap.Telemetry(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "cmd/ESP32-001")]
    public void Command_DownlinkBuildsCorrectTopic(string deviceCode, string expected)
        => MqttTopicMap.Command(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("telemetry/ESP32-001", true, "ESP32-001")]
    [InlineData("heartbeat/DEV-XYZ-99", true, "DEV-XYZ-99")]
    [InlineData("status/A", true, "A")]
    [InlineData("invalid", false, "")]
    [InlineData("telemetry/", false, "")]
    [InlineData("", false, "")]
    public void TryExtractDeviceCode_ParsesPerSpec(string topic, bool expectedOk, string expectedCode)
    {
        var ok = MqttTopicMap.TryExtractDeviceCode(topic, out var code);
        ok.Should().Be(expectedOk);
        if (expectedOk)
            code.Should().Be(expectedCode);
    }
}
