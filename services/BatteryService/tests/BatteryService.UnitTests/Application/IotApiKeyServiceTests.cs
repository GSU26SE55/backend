using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class IotApiKeyServiceTests
{
    private static IIotApiKeyService Build(out MockUnitOfWorkBuilder uow)
    {
        uow = new MockUnitOfWorkBuilder();
        return new IotApiKeyService(uow.Build());
    }

    [Fact]
    public void GenerateKey_ProducesPrefixedRawAndDeterministicHash()
    {
        var svc = Build(out _);
        var key = svc.GenerateKey();

        key.RawKey.Should().StartWith("iotk_");
        key.RawKey.Length.Should().BeGreaterThan(10);
        key.Hash.Length.Should().Be(64);
        key.LastFour.Should().HaveLength(4);
        key.LastFour.Should().Be(key.RawKey[^4..]);

        svc.Hash(key.RawKey).Should().Be(key.Hash);
    }

    [Fact]
    public void VerifyHash_ConstantTimeMatch()
    {
        var svc = Build(out _);
        var key = svc.GenerateKey();

        svc.VerifyHash(key.RawKey, key.Hash).Should().BeTrue();
        svc.VerifyHash(key.Hash, key.Hash).Should().BeTrue();
        svc.VerifyHash("iotk_invalid", key.Hash).Should().BeFalse();
        svc.VerifyHash(string.Empty, key.Hash).Should().BeFalse();
    }

    [Fact]
    public async Task FindDeviceByRawKey_ReturnsDevice_WhenScopeMatches()
    {
        var uow = new MockUnitOfWorkBuilder();
        var svc = new IotApiKeyService(uow.Build());
        var generated = svc.GenerateKey();

        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP32-001",
            ApiKeyHash = generated.Hash,
            ApiKeyLastFour = generated.LastFour,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            Status = IotDeviceStatusEnum.Active,
            ApiKeyIssuedAt = DateTime.UtcNow
        };
        uow.WithIotDevices(device);

        var found = await svc.FindDeviceByRawKeyAsync(generated.RawKey, IotApiKeyScopeEnum.SensorIngest, default);
        found.Should().NotBeNull();
        found!.DeviceCode.Should().Be("ESP32-001");
    }

    [Fact]
    public async Task FindDeviceByRawKey_ReturnsNull_WhenRevoked()
    {
        var uow = new MockUnitOfWorkBuilder();
        var svc = new IotApiKeyService(uow.Build());
        var generated = svc.GenerateKey();

        uow.WithIotDevices(new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP32-002",
            ApiKeyHash = generated.Hash,
            ApiKeyLastFour = generated.LastFour,
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            Status = IotDeviceStatusEnum.Active,
            ApiKeyRevokedAt = DateTime.UtcNow,
            ApiKeyIssuedAt = DateTime.UtcNow.AddDays(-1)
        });

        var found = await svc.FindDeviceByRawKeyAsync(generated.RawKey, IotApiKeyScopeEnum.SensorIngest, default);
        found.Should().BeNull();
    }

    [Fact]
    public async Task FindDeviceByRawKey_ReturnsNull_WhenScopeMissing()
    {
        var uow = new MockUnitOfWorkBuilder();
        var svc = new IotApiKeyService(uow.Build());
        var generated = svc.GenerateKey();

        uow.WithIotDevices(new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "ESP32-003",
            ApiKeyHash = generated.Hash,
            ApiKeyLastFour = generated.LastFour,
            ApiKeyScopes = IotApiKeyScopeEnum.DeviceHeartbeat, // KHÔNG có SensorIngest
            Status = IotDeviceStatusEnum.Active,
            ApiKeyIssuedAt = DateTime.UtcNow
        });

        var found = await svc.FindDeviceByRawKeyAsync(generated.RawKey, IotApiKeyScopeEnum.SensorIngest, default);
        found.Should().BeNull();
    }
}
