using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.Infrastructure.Mqtt;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-784 — credential MQTT vừa cấp không dùng được.
///
/// <para>
/// Đo được lúc chạy thật: tạo device <c>E2E-IOT-230605</c> → API 201 có mqttUsername/password
/// nhưng <c>mqttBrokerHost</c> và <c>mqttBrokerPort</c> đều <b>null</b>. Thiết bị nhận credential
/// mà không biết nối đi đâu.
/// </para>
/// <para>
/// Và kể cả khi biết: ACL Mosquitto dùng <c>pattern write solar/%u/+/telemetry</c> với <c>%u</c> =
/// username (được sinh bằng <c>deviceCode.ToLowerInvariant()</c>), trong khi topic dựng từ
/// deviceCode nguyên bản CHỮ HOA ⇒ không khớp ⇒ broker từ chối dù credential đúng. So khớp topic
/// của MQTT phân biệt hoa/thường và không tắt được.
/// </para>
/// </summary>
public class MqttBrokerEndpointTests
{
    private static MqttBrokerEndpointProvider Provider(
        bool enabled = true, string host = "mosquitto", int port = 8883, bool useTls = true)
        => new(Options.Create(new MqttOptions
        {
            Enabled = enabled,
            Host = host,
            Port = port,
            UseTls = useTls,
        }));

    [Fact]
    public void Resolve_ReturnsUsableEndpoint_WhenMqttEnabled()
    {
        var ep = Provider().Resolve("E2E-IOT-230605");

        ep.Host.Should().Be("mosquitto");
        ep.Port.Should().Be(8883);
        ep.UseTls.Should().BeTrue("thiếu cờ này thiết bị phải đoán TLS từ số cổng");
    }

    [Theory]
    [InlineData("E2E-IOT-230605", "solar/e2e-iot-230605")]
    [InlineData("gw-esp32-mvp-001", "solar/gw-esp32-mvp-001")]
    [InlineData("  MiXeD-Case  ", "solar/mixed-case")]
    public void TopicPrefix_IsAlwaysLowercase_MatchingTheAclUsernamePattern(string deviceCode, string expected)
    {
        // ĐÂY là xung đột case mà issue nêu: ACL dùng %u (chữ thường) còn deviceCode có thể hoa.
        Provider().Resolve(deviceCode).TopicPrefix.Should().Be(expected);
    }

    [Fact]
    public void Resolve_WhenMqttDisabled_ReturnsNothing_RatherThanAnUnusableHost()
    {
        // Thà nói rõ "chưa bật" còn hơn trả host rỗng để thiết bị thử rồi thất bại không hiểu vì sao.
        var ep = Provider(enabled: false).Resolve("E2E-IOT-230605");

        ep.Host.Should().BeNull();
        ep.Port.Should().BeNull();
        ep.TopicPrefix.Should().BeNull();
    }

    [Fact]
    public void Resolve_WhenHostBlank_IsTreatedAsDisabled()
    {
        Provider(host: "   ").Resolve("X").Host.Should().BeNull();
    }

    private static (CreateIotDeviceCommandHandler Handler, MockUnitOfWorkBuilder Uow) MakeHandler(
        global::BatteryService.Application.Interfaces.IMqttBrokerEndpointProvider broker)
    {
        var uow = new MockUnitOfWorkBuilder()
            .WithSites(new Site { Id = SiteId, Name = "Site 1" });
        return (new CreateIotDeviceCommandHandler(uow.Build(), new IotApiKeyService(uow.Build()), broker), uow);
    }

    private static readonly Guid SiteId = Guid.NewGuid();

    private static CreateIotDeviceCommand NewCommand(string deviceCode = "E2E-IOT-230605") => new()
    {
        DeviceCode = deviceCode,
        DisplayName = "Gateway E2E",
        SiteId = SiteId,
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        HeartbeatIntervalSeconds = 60,
    };

    [Fact]
    public async Task CreateDevice_ReturnsBrokerEndpoint_NotNull()
    {
        // Trước bản sửa: DTO có sẵn hai trường này nhưng KHÔNG nơi nào gán ⇒ luôn null.
        var (handler, _) = MakeHandler(TestMqttBrokerEndpointProvider.Enabled("mosquitto", 8883));

        var resp = await handler.Handle(NewCommand(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.MqttBrokerHost.Should().Be("mosquitto");
        resp.Data.MqttBrokerPort.Should().Be(8883);
        resp.Data.MqttUseTls.Should().BeTrue();
    }

    [Fact]
    public async Task CreateDevice_ReturnsLowercaseTopicPrefix_MatchingTheUsername()
    {
        // Username và tiền tố topic PHẢI cùng dạng, nếu không ACL từ chối dù credential đúng.
        var (handler, _) = MakeHandler(TestMqttBrokerEndpointProvider.Enabled());

        var resp = await handler.Handle(NewCommand("E2E-IOT-230605"), CancellationToken.None);

        resp.Data!.MqttUsername.Should().Be("e2e-iot-230605");
        resp.Data.MqttTopicPrefix.Should().Be("solar/e2e-iot-230605");
        resp.Data.MqttTopicPrefix.Should().Be($"solar/{resp.Data.MqttUsername}",
            "tiền tố topic phải khớp CHÍNH XÁC username — đó là điều ACL %u đòi hỏi");
    }

    [Fact]
    public async Task CreateDevice_WhenMqttDisabled_DoesNotHandOutAnEndpoint()
    {
        var (handler, _) = MakeHandler(TestMqttBrokerEndpointProvider.Disabled());

        var resp = await handler.Handle(NewCommand(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue("tắt MQTT không được làm hỏng việc tạo thiết bị");
        resp.Data!.MqttBrokerHost.Should().BeNull();
        resp.Data.MqttUseTls.Should().BeNull();
        resp.Data.MqttTopicPrefix.Should().BeNull();
        // Credential vẫn được cấp và lưu — chỉ là chưa có chỗ để dùng.
        resp.Data.MqttUsername.Should().NotBeNullOrEmpty();
    }
}
