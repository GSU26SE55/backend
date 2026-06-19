using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Admin;
using SmsService.Application.CQRS.Handler.Admin;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Handlers.Admin;

public class RevokeGatewayDeviceCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly RevokeGatewayDeviceCommandHandler _sut;
    private readonly Guid _deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public RevokeGatewayDeviceCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _sut = new RevokeGatewayDeviceCommandHandler(_uow.Object);
    }

    [Fact]
    public async Task Handle_DeviceNotFound_Returns404()
    {
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync((SmsGatewayDevice?)null);

        var resp = await _sut.Handle(new RevokeGatewayDeviceCommand { DeviceId = _deviceId }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Active_RevokesIdempotently()
    {
        var device = new SmsGatewayDevice
        {
            Id = _deviceId,
            DeviceName = "A",
            DeviceCode = "device-A",
            ApiKeyHash = "x",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);

        var resp = await _sut.Handle(new RevokeGatewayDeviceCommand { DeviceId = _deviceId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        device.IsActive.Should().BeFalse();
        device.RevokedAt.Should().NotBeNull();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyRevoked_StillReturns200_Idempotent()
    {
        var device = new SmsGatewayDevice
        {
            Id = _deviceId,
            DeviceName = "A",
            DeviceCode = "device-A",
            ApiKeyHash = "x",
            IsActive = false,
            RevokedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);

        var resp = await _sut.Handle(new RevokeGatewayDeviceCommand { DeviceId = _deviceId }, CancellationToken.None);

        resp.StatusCode.Should().Be(200);
    }
}
