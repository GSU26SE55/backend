using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class CreateIotDeviceHandlerTests
{
    private static (CreateIotDeviceCommandHandler handler, MockUnitOfWorkBuilder uow, IIotApiKeyService keyService) Build()
    {
        var uow = new MockUnitOfWorkBuilder();
        var key = new IotApiKeyService(uow.Build());
        var handler = new CreateIotDeviceCommandHandler(uow.Build(), key, TestMqttBrokerEndpointProvider.Enabled(), NoopMqttPasswordFileSync.Instance());
        return (handler, uow, key);
    }

    [Fact]
    public async Task Create_Succeeds_AndReturnsRawApiKey()
    {
        var (handler, uow, _) = Build();
        var site = new Site { Id = Guid.NewGuid(), Name = "Site A", CustomerId = Guid.NewGuid() };
        uow.WithSites(site);

        var result = await handler.Handle(new CreateIotDeviceCommand
        {
            DeviceCode = "esp32-001",
            DisplayName = "Site A Gateway",
            SiteId = site.Id,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data.Should().NotBeNull();
        result.Data!.RawApiKey.Should().StartWith("iotk_");
        result.Data.DeviceCode.Should().Be("ESP32-001");
        result.Data.ApiKeyLastFour.Should().HaveLength(4);
    }

    [Fact]
    public async Task Create_PersistsPlaintextKey_AndGetByIdReturnsSameKey()
    {
        var (createHandler, uow, _) = Build();
        var site = new Site { Id = Guid.NewGuid(), Name = "Site A", CustomerId = Guid.NewGuid() };
        uow.WithSites(site);

        var created = await createHandler.Handle(new CreateIotDeviceCommand
        {
            DeviceCode = "esp32-persist",
            DisplayName = "Persist test",
            SiteId = site.Id,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60
        }, default);

        created.IsSuccess.Should().BeTrue();
        var rawKey = created.Data!.RawApiKey;
        rawKey.Should().StartWith("iotk_");

        // Đọc lại qua GetById (cùng store) — apiKey phải khớp raw key lúc create.
        var getById = new GetIotDeviceByIdQueryHandler(uow.Build(), TestMqttBrokerEndpointProvider.Enabled());
        var detail = await getById.Handle(new GetIotDeviceByIdQuery { Id = Guid.Parse(created.Data.Id) }, default);

        detail.IsSuccess.Should().BeTrue();
        detail.Data!.ApiKey.Should().Be(rawKey, "GET by id phải trả full plaintext key đã lưu lúc create");
    }

    [Fact]
    public async Task Create_Fails_WhenDeviceCodeDuplicate()
    {
        var (handler, uow, _) = Build();
        var site = new Site { Id = Guid.NewGuid(), Name = "Site A", CustomerId = Guid.NewGuid() };
        uow.WithSites(site)
           .WithIotDevices(new IotDevice
           {
               Id = Guid.NewGuid(),
               DeviceCode = "ESP32-001",
               SiteId = site.Id,
               DisplayName = "existing",
               ApiKeyHash = "x",
               ApiKeyLastFour = "xxxx",
               ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
           });

        var result = await handler.Handle(new CreateIotDeviceCommand
        {
            DeviceCode = "ESP32-001",
            DisplayName = "dup",
            SiteId = site.Id,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_Fails_WhenSiteMissing()
    {
        var (handler, _, _) = Build();
        var result = await handler.Handle(new CreateIotDeviceCommand
        {
            DeviceCode = "ESP32-999",
            DisplayName = "x",
            SiteId = Guid.NewGuid(),
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
