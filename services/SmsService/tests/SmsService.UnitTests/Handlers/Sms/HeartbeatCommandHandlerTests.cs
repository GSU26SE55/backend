using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Handlers.Sms;

public class HeartbeatCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly HeartbeatCommandHandler _sut;
    private readonly Guid _deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public HeartbeatCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _sut = new HeartbeatCommandHandler(_uow.Object);
    }

    [Fact]
    public async Task Handle_UnknownDevice_Returns404()
    {
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync((SmsGatewayDevice?)null);

        var resp = await _sut.Handle(new HeartbeatCommand { DeviceId = _deviceId, Ip = "10.0.0.1" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActiveDevice_TouchesAndReturnsPong()
    {
        var device = new SmsGatewayDevice
        {
            Id = _deviceId,
            DeviceName = "A",
            DeviceCode = "device-A",
            ApiKeyHash = "x",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);

        var resp = await _sut.Handle(new HeartbeatCommand { DeviceId = _deviceId, Ip = "10.0.0.1" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be("pong");
        device.LastSeenIp.Should().Be("10.0.0.1");
        device.LastSeenAt.Should().NotBeNull();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
