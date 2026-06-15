using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.UnitTests.Handlers.Sms;

public class ClaimPendingMessagesCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsMessage>> _msgs = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly Mock<IGenericRepository<SmsAuditLog>> _audits = new();
    private readonly ClaimPendingMessagesCommandHandler _sut;

    private readonly Guid _deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string DeviceCode = "device-A";

    public ClaimPendingMessagesCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsMessages).Returns(_msgs.Object);
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _uow.Setup(u => u.SmsAuditLogs).Returns(_audits.Object);
        _sut = new ClaimPendingMessagesCommandHandler(_uow.Object);
    }

    private SmsGatewayDevice ActiveDevice(int dailyLimit = 100, int sentToday = 0) => new()
    {
        Id = _deviceId,
        DeviceName = "Phone A",
        DeviceCode = DeviceCode,
        ApiKeyHash = "x",
        IsActive = true,
        IsDeleted = false,
        DailyLimit = dailyLimit,
        SentToday = sentToday,
        SentTodayDate = DateOnly.FromDateTime(DateTime.UtcNow),
        CreatedAt = DateTime.UtcNow
    };

    private static SmsMessage PendingMessage(string? target = null, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "+84901234567",
        Message = "hello",
        Status = SmsStatus.Pending,
        TargetDeviceCode = target,
        SourceService = "auth",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = createdAt ?? DateTime.UtcNow,
        IsDeleted = false
    };

    [Fact]
    public async Task Handle_DeviceMissing_Returns403()
    {
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync((SmsGatewayDevice?)null);

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
        resp.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DeviceRevoked_Returns403()
    {
        var device = ActiveDevice();
        device.IsActive = false;
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_DailyLimitReached_ReturnsEmptySilently()
    {
        var device = ActiveDevice(dailyLimit: 100, sentToday: 100);
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new List<SmsMessage>().BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().BeEmpty();
        resp.Message.Should().Contain("Daily limit");
    }

    [Fact]
    public async Task Handle_HasPending_ClaimsAndReturns()
    {
        var device = ActiveDevice();
        var msg1 = PendingMessage();
        var msg2 = PendingMessage(target: DeviceCode);
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { msg1, msg2 }.BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Count.Should().Be(2);
        msg1.Status.Should().Be(SmsStatus.Sending);
        msg2.Status.Should().Be(SmsStatus.Sending);
        msg1.GatewayDeviceCode.Should().Be(DeviceCode);
        _audits.Verify(a => a.AddAsync(It.IsAny<SmsAuditLog>()), Times.Exactly(2));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetMismatch_Excluded()
    {
        var device = ActiveDevice();
        var forUs = PendingMessage(target: DeviceCode);
        var forOther = PendingMessage(target: "device-B");
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { forUs, forOther }.BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.Data.Count.Should().Be(1);
        resp.Data[0].Id.Should().Be(forUs.Id);
    }

    [Fact]
    public async Task Handle_StaleSendingMessage_ReClaimed()
    {
        var device = ActiveDevice();
        // Sending nhưng PickedAt > 5 phút → handler vẫn pick up.
        var stale = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "stale",
            Status = SmsStatus.Sending,
            PickedAt = DateTime.UtcNow.AddMinutes(-10),
            GatewayDeviceCode = "old-device",
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            IsDeleted = false
        };
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { stale }.BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.Data.Count.Should().Be(1);
        stale.GatewayDeviceCode.Should().Be(DeviceCode);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflict_ReturnsEmptyForNextPoll()
    {
        var device = ActiveDevice();
        var msg = PendingMessage();
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { msg }.BuildMock());
        // Race: device khác claim cùng row → xmin conflict.
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("xmin"));

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().BeEmpty();
        resp.Message.Should().Contain("Concurrent");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 20)]   // clamp to max 20
    public async Task Handle_LimitClamped_ToValidRange(int requested, int expectedMax)
    {
        var device = ActiveDevice();
        var messages = Enumerable.Range(0, 30).Select(_ => PendingMessage()).ToArray();
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(messages.BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = requested }, CancellationToken.None);

        resp.Data.Count.Should().BeLessThanOrEqualTo(expectedMax);
        resp.Data.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_DailyAllowance_LimitsTake()
    {
        // Device đã gửi 98/100 hôm nay, request 5 → chỉ được claim 2.
        var device = ActiveDevice(dailyLimit: 100, sentToday: 98);
        var messages = Enumerable.Range(0, 10).Select(_ => PendingMessage()).ToArray();
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);
        _msgs.Setup(m => m.GetAllAsync()).Returns(messages.BuildMock());

        var resp = await _sut.Handle(new ClaimPendingMessagesCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, Limit = 5 }, CancellationToken.None);

        resp.Data.Count.Should().Be(2);
    }
}
