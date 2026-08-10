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

    // IOT3-14 — phân đoạn thiết bị của topic PHẢI là chữ thường.
    //
    // ACL Mosquitto dùng `pattern write solar/%u/...` với %u = username =
    // deviceCode.ToLowerInvariant() (IotApiKeyService.GenerateMqttCredential), trong khi
    // IotDevice.DeviceCode lưu UPPERCASE (CreateIotDeviceCommandHandler.ToUpperInvariant).
    // So khớp topic của MQTT phân biệt hoa/thường và không tắt được ⇒ dựng topic từ
    // DeviceCode nguyên bản là gửi vào chỗ ACL không cho phép, và không bên nào báo lỗi.
    //
    // Trước IOT3-14 hai bài dưới khẳng định NGƯỢC LẠI (giữ nguyên chữ hoa) — tức là chúng
    // khoá chặt đúng cái bug đang có. Serial của pin KHÔNG bị hạ chữ: ACL dùng `+` cho
    // phân đoạn đó nên nó không tham gia so khớp %u.

    [Theory]
    [InlineData("ESP32-001", "BAT-2026-001", "solar/esp32-001/BAT-2026-001/telemetry")]
    [InlineData("esp32-001", "BAT-2026-001", "solar/esp32-001/BAT-2026-001/telemetry")]
    [InlineData("  GW-Mixed-Case  ", "BAT-X", "solar/gw-mixed-case/BAT-X/telemetry")]
    public void Telemetry_BuildsPerDevicePerBatteryTopic(string deviceCode, string serial, string expected)
        => MqttTopicMap.Telemetry(deviceCode, serial).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "solar/esp32-001/cmd")]
    [InlineData("esp32-001", "solar/esp32-001/cmd")]
    [InlineData("GW-ABC", "solar/gw-abc/cmd")]
    public void Command_DownlinkBuildsCorrectTopic(string deviceCode, string expected)
        => MqttTopicMap.Command(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "solar/esp32-001/heartbeat")]
    [InlineData("esp32-001", "solar/esp32-001/heartbeat")]
    [InlineData("  GW-ABC  ", "solar/gw-abc/heartbeat")]
    public void Heartbeat_LowercasesDeviceSegment(string deviceCode, string expected)
        => MqttTopicMap.Heartbeat(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "solar/esp32-001/status")]
    public void Status_LowercasesDeviceSegment(string deviceCode, string expected)
        => MqttTopicMap.Status(deviceCode).Should().Be(expected);

    [Theory]
    [InlineData("ESP32-001", "solar/esp32-001/cmd/ack")]
    public void CommandAck_LowercasesDeviceSegment(string deviceCode, string expected)
        => MqttTopicMap.CommandAck(deviceCode).Should().Be(expected);

    /// <summary>
    /// IOT3-14 — topic thiết bị publish PHẢI khớp topic backend dựng để gửi lệnh xuống.
    /// Đây là bài kiểm chốt: nếu hai đường lệch nhau thì uplink hoặc downlink chết trong im lặng.
    /// </summary>
    [Theory]
    [InlineData("GW-ESP32-001", "gw-esp32-001")]
    [InlineData("gw-esp32-001", "gw-esp32-001")]
    public void DeviceSegment_MatchesMqttUsernameConvention(string deviceCode, string mqttUsername)
    {
        // Quy ước username của IotApiKeyService.GenerateMqttCredential.
        deviceCode.Trim().ToLowerInvariant().Should().Be(mqttUsername);

        MqttTopicMap.Command(deviceCode).Should().Be($"solar/{mqttUsername}/cmd");
        MqttTopicMap.NormalizeDeviceSegment(deviceCode).Should().Be(mqttUsername);
    }

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
