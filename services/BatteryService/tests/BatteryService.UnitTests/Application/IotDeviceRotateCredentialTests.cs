using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// IOT3-86 — hai lệnh xoay khoá, và điểm khác nhau sống còn giữa chúng.
/// </summary>
/// <remarks>
/// <para>
/// <c>rotate-key</c> đổi CẢ apiKey lẫn credential MQTT ⇒ thiết bị mất cả hai đường, <b>phải mang
/// cáp ra hiện trường</b> nạp lại. Trước IOT3-30 nó chỉ đổi apiKey, để nguyên MQTT — thiết bị mất
/// HTTPS (401) nhưng vẫn publish MQTT bằng mật khẩu cũ, tức "xoay khoá" không thật sự thu hồi
/// được gì.
/// </para>
/// <para>
/// <c>rotate-mqtt</c> chỉ đổi phần MQTT và <b>giữ nguyên apiKey</b> ⇒ thiết bị vẫn gọi được
/// <c>/provision</c> để tự lấy mật khẩu mới. Đây là khác biệt LÀM NÊN giá trị của lệnh: một bên
/// tốn một chuyến đi, một bên tự lành. Nếu handler lỡ đụng vào apiKey thì cả hai lệnh thành y hệt
/// nhau và không có gì báo lỗi — nên hai bài test dưới đây chốt đúng chỗ đó.
/// </para>
/// </remarks>
public class IotDeviceRotateCredentialTests
{
    private static IotDevice Device(Guid id) => new()
    {
        Id = id,
        DeviceCode = "GW-ROTATE-01",
        DisplayName = "rotate test",
        SiteId = Guid.NewGuid(),
        Status = IotDeviceStatusEnum.Active,
        ApiKeyHash = "hash-cu",
        ApiKeyPlaintext = "iotk_KHOA_CU",
        ApiKeyLastFour = "u_cu",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-30),
        HeartbeatIntervalSeconds = 60,
        MqttUsername = "gw-rotate-01",
        MqttPasswordHash = "$7$mqtt$cu",
        MqttPasswordPlaintext = "MAT_KHAU_MQTT_CU",
    };

    [Fact]
    public async Task RotateApiKey_RotatesMqttToo_AndReturnsEveryField()
    {
        var id = Guid.NewGuid();
        var device = Device(id);
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var sync = NoopMqttPasswordFileSync.Instance();

        var handler = new RotateIotDeviceApiKeyCommandHandler(
            uow.Build(), new IotApiKeyService(uow.Build()),
            TestMqttBrokerEndpointProvider.Enabled(), sync);

        var result = await handler.Handle(new RotateIotDeviceApiKeyCommand { Id = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);

        // --- apiKey đổi ---
        result.Data!.RawApiKey.Should().NotBeNullOrEmpty().And.NotBe("iotk_KHOA_CU");
        device.ApiKeyHash.Should().NotBe("hash-cu");
        device.ApiKeyRevokedAt.Should().BeNull("xoay khoá là cấp khoá mới, không phải thu hồi thiết bị");

        // --- credential MQTT CŨNG đổi (đây là điểm IOT3-30 sửa) ---
        device.MqttPasswordHash.Should().NotBe("$7$mqtt$cu");
        device.MqttPasswordPlaintext.Should().NotBe("MAT_KHAU_MQTT_CU");
        device.MqttUsername.Should().Be("gw-rotate-01", "username = deviceCode chữ thường, không đổi theo lần xoay");

        // --- DTO đủ trường ---
        // Trước IOT3-31, `RotateIotDeviceApiKeyCommandHandler` gọi mapper mà không truyền broker,
        // nên admin xoay khoá xong nhận về sáu trường MQTT toàn null — không ai báo lỗi.
        result.Data.MqttUsername.Should().Be("gw-rotate-01");
        result.Data.MqttPassword.Should().Be(device.MqttPasswordPlaintext);
        result.Data.MqttBrokerHost.Should().NotBeNullOrEmpty();
        result.Data.MqttBrokerPort.Should().NotBeNull();
        result.Data.MqttUseTls.Should().NotBeNull();
        result.Data.MqttTopicPrefix.Should().Be("solar/gw-rotate-01");
        result.Data.ProvisioningQrCode.Should().Contain(Uri.EscapeDataString(result.Data.RawApiKey));

        // File passwd của broker phải được cập nhật NGAY, không đợi vòng quét nền: giữa hai lần
        // quét, thiết bị nào vừa bị xoay khoá vẫn đăng nhập được bằng mật khẩu cũ.
        sync.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RotateMqtt_LeavesTheApiKeyUntouched()
    {
        var id = Guid.NewGuid();
        var device = Device(id);
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var sync = NoopMqttPasswordFileSync.Instance();

        var handler = new RotateIotDeviceMqttCredentialCommandHandler(
            uow.Build(), new IotApiKeyService(uow.Build()),
            TestMqttBrokerEndpointProvider.Enabled(), sync);

        var result = await handler.Handle(new RotateIotDeviceMqttCredentialCommand { Id = id }, default);

        result.IsSuccess.Should().BeTrue();

        // --- apiKey KHÔNG được đụng vào: đây là toàn bộ lý do lệnh này tồn tại ---
        device.ApiKeyHash.Should().Be("hash-cu");
        device.ApiKeyPlaintext.Should().Be("iotk_KHOA_CU");
        device.ApiKeyLastFour.Should().Be("u_cu");
        result.Data!.RawApiKey.Should().Be("iotk_KHOA_CU",
            "admin phải thấy khoá vẫn còn đó, nếu trả rỗng họ sẽ tưởng vừa làm mất nó");

        // --- credential MQTT đổi ---
        device.MqttPasswordHash.Should().NotBe("$7$mqtt$cu");
        device.MqttPasswordPlaintext.Should().NotBe("MAT_KHAU_MQTT_CU");
        result.Data.MqttPassword.Should().Be(device.MqttPasswordPlaintext);

        sync.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RotateMqtt_Returns404_ForUnknownDevice()
    {
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(Device(Guid.NewGuid()));
        var handler = new RotateIotDeviceMqttCredentialCommandHandler(
            uow.Build(), new IotApiKeyService(uow.Build()),
            TestMqttBrokerEndpointProvider.Enabled(), NoopMqttPasswordFileSync.Instance());

        var result = await handler.Handle(
            new RotateIotDeviceMqttCredentialCommand { Id = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Rotate_StillSucceeds_WhenTheBrokerFileCannotBeWritten()
    {
        // Broker sập hoặc file passwd không ghi được là chuyện của HẠ TẦNG. Để nó làm hỏng cả
        // lệnh xoay khoá thì admin không xoay được khoá vào đúng lúc cần nhất — khi nghi bị lộ.
        // Vòng quét nền sẽ bù phần đồng bộ.
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(Device(id));

        var handler = new RotateIotDeviceApiKeyCommandHandler(
            uow.Build(), new IotApiKeyService(uow.Build()),
            TestMqttBrokerEndpointProvider.Enabled(), NoopMqttPasswordFileSync.Throwing());

        var result = await handler.Handle(new RotateIotDeviceApiKeyCommand { Id = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }
}
