using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Handler.IotDevice;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.UnitTests.Helpers;
using Moq;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// #30 QA solars.io.vn 2026-08-29 — <c>POST /api/iot-devices/{id}/calibrations</c> trả 500 khi FE
/// gửi datetime không có hậu tố "Z" (System.Text.Json deserialize Kind=Unspecified, Npgsql ném lỗi
/// khi insert vào cột timestamptz). Handler phải tự chuẩn hoá Kind=Utc bất kể client gửi gì.
/// </summary>
public class CreateIotDeviceCalibrationHandlerTests
{
    private static (CreateIotDeviceCalibrationCommandHandler handler, MockUnitOfWorkBuilder uow) Build()
    {
        var uow = new MockUnitOfWorkBuilder();
        var cache = new Mock<IIotCalibrationCache>();
        var handler = new CreateIotDeviceCalibrationCommandHandler(uow.Build(), cache.Object);
        return (handler, uow);
    }

    [Fact]
    public async Task Create_CalibratedAtWithUnspecifiedKind_NormalizesToUtc_DoesNotThrow()
    {
        var device = new IotDevice { Id = Guid.NewGuid(), DeviceCode = "ESP32-001" };
        var (handler, uow) = Build();
        uow.WithIotDevices(device);

        var unspecified = DateTime.SpecifyKind(new DateTime(2026, 8, 29, 10, 0, 0), DateTimeKind.Unspecified);
        unspecified.Kind.Should().Be(DateTimeKind.Unspecified);

        var result = await handler.Handle(new CreateIotDeviceCalibrationCommand
        {
            IotDeviceId = device.Id,
            Channel = "voltage",
            Scale = 1.0m,
            Offset = 0m,
            Unit = "V",
            CalibratedAt = unspecified,
            ExpiresAt = unspecified.AddYears(1)
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.CalibratedAt.Kind.Should().Be(DateTimeKind.Utc);
        result.Data.ExpiresAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
}
