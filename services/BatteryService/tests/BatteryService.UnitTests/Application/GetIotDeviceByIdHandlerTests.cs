using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Unit test cho <see cref="GetIotDeviceByIdQueryHandler"/> — endpoint <c>GET /api/admin/iot-devices/{id}</c>.
/// Trọng tâm: trả full plaintext <c>apiKey</c> (từ <c>IotDevice.ApiKeyPlaintext</c>) cho Admin xem lại.
/// </summary>
public class GetIotDeviceByIdHandlerTests
{
    private static IotDevice Device(Guid id, Guid siteId, string? apiKeyPlaintext, bool isDeleted = false) => new()
    {
        Id = id,
        DeviceCode = "ESP32-001",
        DisplayName = "Edge device",
        SiteId = siteId,
        Status = IotDeviceStatusEnum.Active,
        ApiKeyHash = "hash",
        ApiKeyPlaintext = apiKeyPlaintext,
        ApiKeyLastFour = "aB12",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-1),
        HeartbeatIntervalSeconds = 60,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task Returns200_WithFullPlaintextApiKey()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(id, Guid.NewGuid(), "iotk_full-plaintext-key-aB12"));
        var handler = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new GetIotDeviceByIdQuery { Id = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(id.ToString());
        result.Data.ApiKey.Should().Be("iotk_full-plaintext-key-aB12");
        // last-four vẫn có (backward compatible).
        result.Data.ApiKeyLastFour.Should().Be("aB12");
    }

    [Fact]
    public async Task Returns200_WithNullApiKey_WhenLegacyDeviceHasNoPlaintext()
    {
        // Device tạo trước khi bật lưu plaintext → ApiKeyPlaintext = null.
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(id, Guid.NewGuid(), apiKeyPlaintext: null));
        var handler = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new GetIotDeviceByIdQuery { Id = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.ApiKey.Should().BeNull();
        result.Data.ApiKeyLastFour.Should().Be("aB12");
    }

    [Fact]
    public async Task Returns404_WhenNotFound()
    {
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(Guid.NewGuid(), Guid.NewGuid(), "iotk_x"));
        var handler = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new GetIotDeviceByIdQuery { Id = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Data.Should().BeNull();
        result.ListErrors.Should().BeEmpty("404 không phải field-validation");
    }

    [Fact]
    public async Task Returns404_WhenSoftDeleted()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(id, Guid.NewGuid(), "iotk_x", isDeleted: true));
        var handler = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new GetIotDeviceByIdQuery { Id = id }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MapsSiteName_WhenSiteNavigationPresent()
    {
        var id = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var device = Device(id, siteId, "iotk_x");
        device.Site = new Site { Id = siteId, Name = "Solar Farm A" };
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());

        var result = await handler.Handle(new GetIotDeviceByIdQuery { Id = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.SiteId.Should().Be(siteId.ToString());
        result.Data.SiteName.Should().Be("Solar Farm A");
    }
}
