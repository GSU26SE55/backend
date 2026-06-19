using MockQueryable.Moq;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Handler.Admin;
using SmsService.Application.CQRS.Query.Admin;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Handlers.Admin;

public class ListGatewayDevicesQueryHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly ListGatewayDevicesQueryHandler _sut;

    public ListGatewayDevicesQueryHandlerTests()
    {
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _sut = new ListGatewayDevicesQueryHandler(_uow.Object);
    }

    private static SmsGatewayDevice Device(string code, bool active, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        DeviceName = $"Phone {code}",
        DeviceCode = code,
        ApiKeyHash = "x",
        IsActive = active,
        IsDeleted = deleted,
        DailyLimit = 100,
        CreatedAt = DateTime.UtcNow.AddMinutes(-Random.Shared.Next(1, 1000))
    };

    [Fact]
    public async Task Handle_IncludeRevoked_ReturnsActivePlusRevoked()
    {
        var d1 = Device("active-1", active: true);
        var d2 = Device("revoked-1", active: false);
        var deleted = Device("soft-deleted", active: true, deleted: true);
        _devices.Setup(d => d.GetAllAsync()).Returns(new[] { d1, d2, deleted }.BuildMock());

        var resp = await _sut.Handle(new ListGatewayDevicesQuery { IncludeRevoked = true }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().HaveCount(2);
        resp.Data.Should().NotContain(d => d.DeviceCode == "soft-deleted");
    }

    [Fact]
    public async Task Handle_ExcludeRevoked_OnlyActive()
    {
        var d1 = Device("active-1", active: true);
        var d2 = Device("revoked-1", active: false);
        _devices.Setup(d => d.GetAllAsync()).Returns(new[] { d1, d2 }.BuildMock());

        var resp = await _sut.Handle(new ListGatewayDevicesQuery { IncludeRevoked = false }, CancellationToken.None);

        resp.Data.Should().HaveCount(1);
        resp.Data[0].DeviceCode.Should().Be("active-1");
    }

    [Fact]
    public async Task Handle_NoData_ReturnsEmptySuccess()
    {
        _devices.Setup(d => d.GetAllAsync()).Returns(new List<SmsGatewayDevice>().BuildMock());

        var resp = await _sut.Handle(new ListGatewayDevicesQuery { }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().BeEmpty();
    }
}
