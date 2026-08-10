using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// IOT3-85 — chốt hành vi <c>/provision</c> sau IOT3-26..29 + IOT3-78.
/// </summary>
/// <remarks>
/// Mấu chốt của Phương án A nằm ở đây: thiết bị chỉ cần <c>deviceCode</c> + <c>apiKey</c> nạp tay,
/// mọi thứ còn lại (broker ở đâu, đăng nhập bằng gì, topic tiền tố nào, đo mấy giây một lần) đều
/// do backend cấp trong chính response này. Sai ở đây thì thiết bị lại phải nhúng cứng cấu hình
/// MQTT vào firmware — đúng cái sprint muốn xoá bỏ.
/// </remarks>
public class ProvisionMqttConfigTests
{
    private static IotDevice Device(
        Guid id,
        string deviceCode = "GW-ESP32-001",
        string? mqttUsername = null,
        string? mqttHash = null,
        string? mqttPlaintext = null,
        int pollingSeconds = 10) => new()
    {
        Id = id,
        DeviceCode = deviceCode,
        DisplayName = "test",
        SiteId = Guid.NewGuid(),
        Status = IotDeviceStatusEnum.Pending,
        ApiKeyHash = "hash",
        ApiKeyLastFour = "abcd",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-1),
        HeartbeatIntervalSeconds = 60,
        PollingIntervalSeconds = pollingSeconds,
        MqttUsername = mqttUsername,
        MqttPasswordHash = mqttHash,
        MqttPasswordPlaintext = mqttPlaintext
    };

    private static ProvisionIotDeviceCommand Cmd(Guid id, string deviceCode = "GW-ESP32-001") => new()
    {
        DeviceId = id,
        DeviceCode = deviceCode,
        FirmwareVersion = "1.0.0",
        HardwareRevision = "v1.0",
        DeviceTimestamp = DateTime.UtcNow
    };

    // ---------------------------------------------------------------- IOT3-27

    [Fact]
    public async Task Provision_WhenMqttEnabled_HandsOutAllSixFields()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(
            Device(id, mqttUsername: "gw-esp32-001", mqttHash: "$7$hash", mqttPlaintext: "pw-123"));
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled("mqtt.local", 8883, useTls: true),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var res = await handler.Handle(Cmd(id), default);

        res.IsSuccess.Should().BeTrue();
        res.Data!.MqttBrokerHost.Should().Be("mqtt.local");
        res.Data.MqttBrokerPort.Should().Be(8883);
        res.Data.MqttUseTls.Should().BeTrue();
        res.Data.MqttUsername.Should().Be("gw-esp32-001");
        res.Data.MqttPassword.Should().Be("pw-123");

        // Tiền tố topic phải khớp CHÍNH XÁC username — ACL Mosquitto dùng `solar/%u/...`
        // và so khớp topic phân biệt hoa/thường, không tắt được.
        res.Data.MqttTopicPrefix.Should().Be("solar/gw-esp32-001");
    }

    [Fact]
    public async Task Provision_WhenMqttDisabled_LeavesAllSixNull()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(
            Device(id, mqttUsername: "gw-esp32-001", mqttHash: "$7$hash", mqttPlaintext: "pw-123"));
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Disabled(),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var res = await handler.Handle(Cmd(id), default);

        res.IsSuccess.Should().BeTrue("MQTT tắt không được làm provision thất bại — thiết bị chạy HTTPS-only");

        // CẢ SÁU cùng null. Trả nửa vời (có username, thiếu host) khiến firmware thử nối
        // rồi thất bại trong vòng lặp mà không có cách nào biết vì sao.
        res.Data!.MqttBrokerHost.Should().BeNull();
        res.Data.MqttBrokerPort.Should().BeNull();
        res.Data.MqttUseTls.Should().BeNull();
        res.Data.MqttTopicPrefix.Should().BeNull();
        res.Data.MqttUsername.Should().BeNull();
        res.Data.MqttPassword.Should().BeNull();
    }

    // ---------------------------------------------------------------- IOT3-28

    [Fact]
    public async Task Provision_WhenCredentialMissing_GeneratesAndPersistsIt()
    {
        var id = Guid.NewGuid();
        // Thiết bị tạo trước #IoT2-26: chưa có username lẫn hash.
        var device = Device(id, mqttUsername: null, mqttHash: null, mqttPlaintext: null);
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var sync = NoopMqttPasswordFileSync.Instance();
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), sync);

        var res = await handler.Handle(Cmd(id), default);

        res.IsSuccess.Should().BeTrue();
        res.Data!.MqttUsername.Should().Be("gw-esp32-001", "username = deviceCode chữ thường");
        res.Data.MqttPassword.Should().NotBeNullOrEmpty();

        // Phải LƯU vào entity, không chỉ trả về — lần provision sau phải ra cùng mật khẩu.
        device.MqttUsername.Should().Be("gw-esp32-001");
        device.MqttPasswordHash.Should().StartWith("$7$", "định dạng Mosquitto PBKDF2-SHA512");
        device.MqttPasswordPlaintext.Should().Be(res.Data.MqttPassword);

        // IOT3-29 — vừa sinh mới thì phải đẩy xuống broker NGAY, không đợi vòng quét 60s.
        sync.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Provision_WhenOnlyPlaintextMissing_StillRegenerates()
    {
        var id = Guid.NewGuid();
        // Thiết bị tạo sau #IoT2-26 nhưng trước IOT3-25: có hash, KHÔNG có plaintext.
        // Hash là PBKDF2 một chiều nên không dựng lại mật khẩu được ⇒ buộc phải sinh mới.
        var device = Device(id, mqttUsername: "gw-esp32-001", mqttHash: "$7$old", mqttPlaintext: null);
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var res = await handler.Handle(Cmd(id), default);

        res.Data!.MqttPassword.Should().NotBeNullOrEmpty();
        device.MqttPasswordHash.Should().NotBe("$7$old", "hash cũ không khớp mật khẩu mới nữa");
        device.MqttPasswordPlaintext.Should().Be(res.Data.MqttPassword);
    }

    [Fact]
    public async Task Provision_WhenCredentialAlreadyComplete_DoesNotTouchIt()
    {
        var id = Guid.NewGuid();
        var device = Device(id, mqttUsername: "gw-esp32-001", mqttHash: "$7$keep", mqttPlaintext: "keep-me");
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var sync = NoopMqttPasswordFileSync.Instance();
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), sync);

        await handler.Handle(Cmd(id), default);

        // Xoay mật khẩu ở MỖI lần boot sẽ tạo cửa sổ đua: thiết bị nhận mật khẩu mới trước khi
        // broker kịp nạp lại file passwd ⇒ bị từ chối rồi mới tự lành. Không đổi thì không có đua.
        device.MqttPasswordHash.Should().Be("$7$keep");
        device.MqttPasswordPlaintext.Should().Be("keep-me");
        sync.CallCount.Should().Be(0, "không sinh mới thì không cần bắt broker nạp lại");
    }

    // ---------------------------------------------------------------- IOT3-29

    [Fact]
    public async Task Provision_WhenPasswordFileSyncThrows_StillSucceeds()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(Device(id));
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Throwing());

        var res = await handler.Handle(Cmd(id), default);

        // Sự cố hạ tầng (mount read-only, đĩa đầy, broker chưa lên) KHÔNG được biến thành
        // "thiết bị không boot được". Đường HTTPS vẫn dùng tốt và vòng quét nền sẽ bù.
        res.IsSuccess.Should().BeTrue("đồng bộ passwd hỏng không được làm provision thất bại");
        res.StatusCode.Should().Be(200);
    }

    // ---------------------------------------------------------------- IOT3-78

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(600)]
    public async Task Provision_ReturnsPollingIntervalFromDatabase(int seconds)
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(Device(id, pollingSeconds: seconds));
        var handler = new ProvisionIotDeviceCommandHandler(
            uow.Build(), TestMqttBrokerEndpointProvider.Enabled(),
            new IotApiKeyService(uow.Build()), NoopMqttPasswordFileSync.Instance());

        var res = await handler.Handle(Cmd(id), default);

        // Trước IOT3-78 giá trị này là số cứng 10 trong handler ⇒ Admin không đổi được qua web.
        res.Data!.PollingIntervalSeconds.Should().Be(seconds);
    }
}
