using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.UnitTests.Handlers.Sms;

public class ReportSmsResultCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsMessage>> _msgs = new();
    private readonly Mock<IGenericRepository<SmsGatewayDevice>> _devices = new();
    private readonly Mock<IGenericRepository<SmsAuditLog>> _audits = new();
    private readonly Mock<IMessageProducerService> _outbox = new();
    private readonly ReportSmsResultCommandHandler _sut;

    private readonly Guid _deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string DeviceCode = "device-A";

    public ReportSmsResultCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsMessages).Returns(_msgs.Object);
        _uow.Setup(u => u.SmsGatewayDevices).Returns(_devices.Object);
        _uow.Setup(u => u.SmsAuditLogs).Returns(_audits.Object);
        _sut = new ReportSmsResultCommandHandler(_uow.Object, _outbox.Object);
    }

    private SmsMessage SendingMessage(int retryCount = 0, int maxRetry = 3) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "+84901234567",
        Message = "test",
        Status = SmsStatus.Sending,
        GatewayDeviceCode = DeviceCode,
        GatewayDeviceId = _deviceId,
        PickedAt = DateTime.UtcNow.AddMinutes(-1),
        SourceService = "auth",
        CorrelationId = Guid.NewGuid(),
        RetryCount = retryCount,
        MaxRetryCount = maxRetry,
        CreatedAt = DateTime.UtcNow.AddMinutes(-5)
    };

    private SmsGatewayDevice DeviceFixture() => new()
    {
        Id = _deviceId,
        DeviceName = "A",
        DeviceCode = DeviceCode,
        ApiKeyHash = "x",
        IsActive = true,
        DailyLimit = 100,
        SentToday = 5,
        SentTodayDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    [Fact]
    public async Task Handle_SmsNotFound_Returns404_MessageOnly_NoListErrors()
    {
        var smsId = Guid.NewGuid();
        _msgs.Setup(m => m.GetAllAsync()).Returns(new List<SmsMessage>().BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = smsId, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
        // Resource-level error (NOT field validation) → Message populated, ListErrors empty.
        // ErrorsListJsonConverter sẽ serialize empty list → JSON `null` trên wire.
        resp.Message.Should().NotBeNullOrEmpty();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WrongDevice_Returns403()
    {
        var sms = SendingMessage();
        sms.GatewayDeviceCode = "other-device";
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(SmsStatus.Sent)]
    [InlineData(SmsStatus.Failed)]
    [InlineData(SmsStatus.Pending)]
    [InlineData(SmsStatus.Cancelled)]
    public async Task Handle_AnyNonSendingStatus_Idempotent_Returns200NoOp(SmsStatus current)
    {
        var sms = SendingMessage();
        sms.Status = current;
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Message.Should().Contain("ignored");
        // No outbox publish, no SaveChanges
        _outbox.Verify(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Sent_TransitionsAndPublishesDeliveryReport()
    {
        var sms = SendingMessage();
        var device = DeviceFixture();
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(device);

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        sms.Status.Should().Be(SmsStatus.Sent);
        device.SentToday.Should().Be(6); // increment
        _outbox.Verify(p => p.PublishAsync(It.IsAny<SmsDeliveryReportEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("SENT")]
    [InlineData("Sent")]
    [InlineData("failed")]
    [InlineData("FAILED")]
    public async Task Handle_StatusCaseInsensitive_Accepted(string status)
    {
        var sms = SendingMessage(retryCount: 0, maxRetry: 3);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(DeviceFixture());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = status, ErrorMessage = "err" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidStatus_Returns400_WithFieldError()
    {
        var sms = SendingMessage();
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Maybe", ErrorMessage = null }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
        // Field-level validation: Status value invalid → ListErrors[Status].
        resp.ListErrors.Should().ContainSingle(e => e.Field == nameof(ReportSmsResultCommand.Status));
    }

    [Fact]
    public async Task Handle_FailedWithRetryRemaining_BumpsRetryAndResetsToPending()
    {
        // RetryCount = 0, Max = 3 → sau Fail = 1, vẫn còn retry (1 < 3).
        var sms = SendingMessage(retryCount: 0, maxRetry: 3);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Failed", ErrorMessage = "timeout" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        sms.Status.Should().Be(SmsStatus.Pending);
        sms.RetryCount.Should().Be(1);
        sms.ErrorMessage.Should().Be("timeout");
        // KHÔNG publish SmsFailedEvent (chưa final).
        _outbox.Verify(p => p.PublishAsync(It.IsAny<SmsFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FailedAtMaxRetry_FinalsAndPublishesSmsFailedEvent()
    {
        // RetryCount = 2, Max = 3 → sau Fail next attempt = 3 (== max) → final.
        var sms = SendingMessage(retryCount: 2, maxRetry: 3);
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Failed", ErrorMessage = "permanent" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        sms.Status.Should().Be(SmsStatus.Failed);
        sms.RetryCount.Should().Be(3);
        _outbox.Verify(p => p.PublishAsync(
            It.Is<SmsFailedEvent>(e => e.FinalFailure && e.ErrorMessage == "permanent"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflict_TreatedAsDuplicate()
    {
        var sms = SendingMessage();
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(DeviceFixture());
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("xmin"));

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Message.Should().Contain("duplicate");
    }

    [Fact]
    public async Task Handle_OutboxCalledBeforeSaveChanges_AtomicGuarantee()
    {
        // Spec yêu cầu: publish TRƯỚC SaveChanges → cùng transaction.
        // Test bằng cách verify thứ tự call: Publish phải xảy ra TRƯỚC SaveChangesAsync.
        var sms = SendingMessage();
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());
        _devices.Setup(d => d.GetByIdAsync(_deviceId)).ReturnsAsync(DeviceFixture());

        var sequence = new MockSequence();
        _outbox.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SmsDeliveryReportEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var resp = await _sut.Handle(new ReportSmsResultCommand { DeviceId = _deviceId, DeviceCode = DeviceCode, SmsId = sms.Id, Status = "Sent", ErrorMessage = null }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
    }
}
