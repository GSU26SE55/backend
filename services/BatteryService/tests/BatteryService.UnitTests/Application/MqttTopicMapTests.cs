using BatteryService.Infrastructure.Mqtt;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint IoT-2 #IoT2-22 — verify topic builder + reverse extraction theo schema mới (solar/+/...).
/// Wildcard pattern phải khớp ACL trong infra/mqtt/acl.conf.
/// </summary>
public class MqttTopicMapTests
{
    [Theory]
    [InlineData("solar/+/+/telemetry")]
    [InlineData("solar/+/heartbeat")]
    [InlineData("solar/+/status")]
    [InlineData("solar/+/cmd/ack")]
    public void Wildcards_FollowNewSchema(string expected)
    {
        var actual = expected switch
        {
            "solar/+/+/telemetry" => MqttTopicMap.TelemetryWildcard,
            "solar/+/heartbeat" => MqttTopicMap.HeartbeatWildcard,
            "solar/+/status" => MqttTopicMap.StatusWildcard,
            "solar/+/cmd/ack" => MqttTopicMap.CommandAckWildcard,
            _ => string.Empty
        };
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("ESP32-001", "BAT-2026-001", "solar/ESP32-001/BAT-2026-001/telemetry")]
    public void Telemetry_BuildsPerDevicePerBatteryTopic(string deviceCode, string serial, string expected)
        => MqttTopicMap.Telemetry(deviceCode, serial).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "solar/ESP32-001/cmd")]
    public void Command_DownlinkBuildsCorrectTopic(string deviceCode, string expected)
        => MqttTopicMap.Command(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("solar/ESP32-001/BAT-001/telemetry", true, "ESP32-001", "telemetry", "BAT-001")]
    [InlineData("solar/DEV-XYZ-99/heartbeat", true, "DEV-XYZ-99", "heartbeat", null)]
    [InlineData("solar/A/status", true, "A", "status", null)]
    [InlineData("solar/A/cmd/ack", true, "A", "cmd_ack", null)]
    [InlineData("invalid", false, "", "", null)]
    [InlineData("solar/", false, "", "", null)]
    [InlineData("", false, "", "", null)]
    public void TryParse_HandlesAllKnownTopics(string topic, bool expectedOk, string expectedCode, string expectedKind, string? expectedSerial)
    {
        var ok = MqttTopicMap.TryParse(topic, out var code, out var kind, out var serial);
        ok.Should().Be(expectedOk);
        if (expectedOk)
        {
            code.Should().Be(expectedCode);
            kind.Should().Be(expectedKind);
            serial.Should().Be(expectedSerial);
        }
    }
}
