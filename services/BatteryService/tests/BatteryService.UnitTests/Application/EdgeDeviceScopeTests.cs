using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-785 — scope mặc định chặn đúng những cảm biến mà firmware đã mang sẵn.
///
/// <para>
/// Đo được lúc chạy thật: tạo device không truyền scopes → <c>apiKeyScopes=11</c>. Cùng khoá đó,
/// provision / sensor ingest / heartbeat / firmware-check đều PASS, nhưng ambient batch và
/// environmental incident đều <b>401</b>. Nâng scope lên 15 thì ambient trả 201 — chứng minh
/// vấn đề nằm ở quyền chứ không phải xác thực.
/// </para>
/// <para>
/// Đây không phải bất tiện nhỏ: đó là đường báo khói, gas và rò nước. Thiết bị chạy bình thường,
/// telemetry vào đều, nên không ai nghi ngờ gì cho tới lúc cần cảnh báo an toàn thì nó im.
/// </para>
/// </summary>
public class EdgeDeviceScopeTests
{
    [Fact]
    public void EdgeDeviceDefault_IncludesEnvironmentalIngest()
    {
        // Firmware xuất xưởng có SHT31 (nhiệt/ẩm), MQ2 (gas/khói) và cảm biến rò nước — bundle mặc
        // định phải phủ hết những gì phần cứng gửi được.
        IotApiKeyScopeEnum.EdgeDeviceDefault.Should().HaveFlag(IotApiKeyScopeEnum.EnvironmentalIngest);
    }

    [Fact]
    public void EdgeDeviceDefault_StillIncludesEverythingItHadBefore()
    {
        // Chống hồi quy: thêm scope không được làm rơi scope cũ.
        IotApiKeyScopeEnum.EdgeDeviceDefault.Should().HaveFlag(IotApiKeyScopeEnum.SensorIngest);
        IotApiKeyScopeEnum.EdgeDeviceDefault.Should().HaveFlag(IotApiKeyScopeEnum.DeviceHeartbeat);
        IotApiKeyScopeEnum.EdgeDeviceDefault.Should().HaveFlag(IotApiKeyScopeEnum.FirmwareCheck);
        ((int)IotApiKeyScopeEnum.EdgeDeviceDefault).Should().Be(15);
    }

    private static (IotApiKeyService Service, string RawKey) SeedDevice(IotApiKeyScopeEnum scopes)
    {
        var uow = new MockUnitOfWorkBuilder();
        var service = new IotApiKeyService(uow.Build());
        var key = service.GenerateKey();

        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = "GW-785",
            DisplayName = "Gateway",
            SiteId = Guid.NewGuid(),
            Status = IotDeviceStatusEnum.Active,
            ApiKeyHash = key.Hash,
            ApiKeyLastFour = key.LastFour,
            ApiKeyScopes = scopes,
            HeartbeatIntervalSeconds = 60,
        };

        return (new IotApiKeyService(new MockUnitOfWorkBuilder().WithIotDevices(device).Build()), key.RawKey);
    }

    [Fact]
    public async Task Lookup_ValidKeyWithScope_ReturnsDevice()
    {
        var (service, rawKey) = SeedDevice(IotApiKeyScopeEnum.EdgeDeviceDefault);

        var result = await service.LookupDeviceByRawKeyAsync(
            rawKey, IotApiKeyScopeEnum.EnvironmentalIngest, CancellationToken.None);

        result.Device.Should().NotBeNull();
        result.ScopeDenied.Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_ValidKeyMissingScope_IsScopeDenied_NotNotFound()
    {
        // ĐÂY là điểm mấu chốt: khoá HỢP LỆ nhưng thiếu quyền. Gộp với "khoá sai" thành 401 khiến
        // người vận hành đi xoay khoá, cấp lại khoá — mà không bao giờ nhận ra vấn đề là thiếu scope.
        var (service, rawKey) = SeedDevice(
            IotApiKeyScopeEnum.SensorIngest | IotApiKeyScopeEnum.DeviceHeartbeat);

        var result = await service.LookupDeviceByRawKeyAsync(
            rawKey, IotApiKeyScopeEnum.EnvironmentalIngest, CancellationToken.None);

        result.Device.Should().BeNull();
        result.ScopeDenied.Should().BeTrue("khoá đúng + thiếu quyền là 403, không phải 401");
    }

    [Fact]
    public async Task Lookup_UnknownKey_IsNotFound_NotScopeDenied()
    {
        // Chống nhầm chiều ngược lại: khoá bịa ra KHÔNG được trả 403 — 403 xác nhận khoá đó có
        // thật, biến endpoint thành công cụ dò khoá hợp lệ.
        var (service, _) = SeedDevice(IotApiKeyScopeEnum.EdgeDeviceDefault);

        var result = await service.LookupDeviceByRawKeyAsync(
            "iotk_khong-ton-tai", IotApiKeyScopeEnum.SensorIngest, CancellationToken.None);

        result.Device.Should().BeNull();
        result.ScopeDenied.Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_MalformedKey_IsNotFound()
    {
        var (service, _) = SeedDevice(IotApiKeyScopeEnum.EdgeDeviceDefault);

        var result = await service.LookupDeviceByRawKeyAsync(
            "khong-co-tien-to", IotApiKeyScopeEnum.SensorIngest, CancellationToken.None);

        result.Device.Should().BeNull();
        result.ScopeDenied.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultProvisionedDevice_CanIngestEnvironmentalData()
    {
        // Kịch bản đầu-cuối của issue: thiết bị tạo theo mặc định phải gửi được dữ liệu môi trường.
        var (service, rawKey) = SeedDevice(IotApiKeyScopeEnum.EdgeDeviceDefault);

        foreach (var scope in new[]
                 {
                     IotApiKeyScopeEnum.SensorIngest,
                     IotApiKeyScopeEnum.DeviceHeartbeat,
                     IotApiKeyScopeEnum.EnvironmentalIngest,
                     IotApiKeyScopeEnum.FirmwareCheck,
                 })
        {
            var result = await service.LookupDeviceByRawKeyAsync(rawKey, scope, CancellationToken.None);
            result.Device.Should().NotBeNull($"scope {scope} phải nằm trong bundle mặc định");
        }
    }
}
