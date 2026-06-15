using MockQueryable.Moq;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Admin;
using SmsService.Application.CQRS.Handler.Admin;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Handlers.Admin;

public class CreateGatewayDeviceCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly Mock<IGatewayApiKeyHasher> _hasher = new();
    private readonly CreateGatewayDeviceCommandHandler _sut;

    public CreateGatewayDeviceCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"hashed::{p}");
        _sut = new CreateGatewayDeviceCommandHandler(_uow.Object, _hasher.Object);
    }

    [Fact]
    public async Task Handle_NewDeviceCode_CreatesAndReturnsPlaintextApiKey()
    {
        _devices.Setup(d => d.GetAllAsync()).Returns(new List<SmsGatewayDevice>().BuildMock());

        var cmd = new CreateGatewayDeviceCommand
        {
            DeviceName = "Phone A",
            DeviceCode = "android-001",
            DailyLimit = 200
        };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        // POST create new resource → 201 Created (RFC 7231, đồng nhất với AuthService.CreateRole/Battery.CreateAsset).
        resp.StatusCode.Should().Be(201);
        resp.Data.Id.Should().NotBe(Guid.Empty);
        resp.Data.DeviceCode.Should().Be("android-001");
        resp.Data.ApiKey.Should().NotBeNullOrEmpty();
        // Plaintext NOT hashed in response, but DB has hash
        _devices.Verify(d => d.AddAsync(It.Is<SmsGatewayDevice>(s =>
            s.DeviceCode == "android-001" &&
            s.ApiKeyHash.StartsWith("hashed::") &&
            s.DailyLimit == 200 &&
            s.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateCode_Returns409()
    {
        var existing = new SmsGatewayDevice
        {
            Id = Guid.NewGuid(),
            DeviceName = "X",
            DeviceCode = "android-001",
            ApiKeyHash = "x",
            CreatedAt = DateTime.UtcNow
        };
        _devices.Setup(d => d.GetAllAsync()).Returns(new[] { existing }.BuildMock());

        var cmd = new CreateGatewayDeviceCommand { DeviceName = "Another", DeviceCode = "android-001" };
        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        _devices.Verify(d => d.AddAsync(It.IsAny<SmsGatewayDevice>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_MissingFields_ReportsErrors()
    {
        var cmd = new CreateGatewayDeviceCommand
        {
            DeviceName = "",
            DeviceCode = "",
            DailyLimit = 0
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == nameof(cmd.DeviceName));
        resp.ListErrors.Should().Contain(e => e.Field == nameof(cmd.DeviceCode));
        resp.ListErrors.Should().Contain(e => e.Field == nameof(cmd.DailyLimit));
    }

    [Fact]
    public async Task ValidateAsync_Valid_Passes()
    {
        var cmd = new CreateGatewayDeviceCommand
        {
            DeviceName = "Phone A",
            DeviceCode = "android-001",
            DailyLimit = 100
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }
}
