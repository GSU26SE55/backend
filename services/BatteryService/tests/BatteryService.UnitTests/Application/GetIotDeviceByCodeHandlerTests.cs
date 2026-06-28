using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Unit test cho <see cref="GetIotDeviceByCodeQueryHandler"/> — endpoint <c>GET /api/iot-devices/by-code/{deviceCode}</c>
/// cho Staff resolve <c>deviceCode → device.Id (GUID)</c> trước khi gọi calibration API.
/// </summary>
public class GetIotDeviceByCodeHandlerTests
{
    // DeviceCode được Create lưu dạng Trim().ToUpperInvariant() — seed giống dữ liệu thật.
    private static IotDevice Device(Guid id, string code, Guid siteId, bool isDeleted = false) => new()
    {
        Id = id,
        DeviceCode = code,
        DisplayName = "Edge device",
        SiteId = siteId,
        Status = IotDeviceStatusEnum.Active,
        ApiKeyHash = "hash",
        ApiKeyLastFour = "abcd",
        ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
        ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-1),
        HeartbeatIntervalSeconds = 60,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task ReturnsDevice_WhenCodeMatchesExactly()
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(id, "ESP32-SIM-001", Guid.NewGuid()));
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = "ESP32-SIM-001" }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(id.ToString());
        result.Data.DeviceCode.Should().Be("ESP32-SIM-001");
    }

    [Theory]
    [InlineData("esp32-sim-001")]          // lowercase
    [InlineData("  ESP32-SIM-001  ")]      // thừa whitespace
    [InlineData("  esp32-sim-001  ")]      // lowercase + whitespace
    public async Task Lookup_IsCaseInsensitive_AndTrimsWhitespace(string input)
    {
        var id = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(id, "ESP32-SIM-001", Guid.NewGuid()));
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = input }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(id.ToString());
    }

    [Fact]
    public async Task Returns404_WhenNoDeviceMatches()
    {
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(Guid.NewGuid(), "ESP32-SIM-001", Guid.NewGuid()));
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = "ESP32-DOES-NOT-EXIST" }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Returns404_WhenDeviceSoftDeleted()
    {
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(Guid.NewGuid(), "ESP32-SIM-001", Guid.NewGuid(), isDeleted: true));
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = "ESP32-SIM-001" }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns404_WhenCodeEmptyOrWhitespace(string input)
    {
        var uow = new MockUnitOfWorkBuilder()
            .WithIotDevices(Device(Guid.NewGuid(), "ESP32-SIM-001", Guid.NewGuid()));
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = input }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MapsSiteName_WhenSiteNavigationPresent()
    {
        var id = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var device = Device(id, "ESP32-SIM-001", siteId);
        device.Site = new Site { Id = siteId, Name = "Solar Farm A" };
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(device);
        var handler = new GetIotDeviceByCodeQueryHandler(uow.Build());

        var result = await handler.Handle(new GetIotDeviceByCodeQuery { DeviceCode = "ESP32-SIM-001" }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.SiteId.Should().Be(siteId.ToString());
        result.Data.SiteName.Should().Be("Solar Farm A");
    }
}
