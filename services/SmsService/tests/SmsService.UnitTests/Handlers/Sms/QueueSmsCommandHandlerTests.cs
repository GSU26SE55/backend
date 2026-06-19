using Microsoft.Extensions.Logging.Abstractions;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Handlers.Sms;

public class QueueSmsCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsMessage>> _msgs = new();
    private readonly Mock<IGenericRepository<SmsAuditLog>> _audits = new();
    private readonly Mock<ISmsGatewayNotifier> _notifier = new();
    private readonly QueueSmsCommandHandler _sut;

    public QueueSmsCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsMessages).Returns(_msgs.Object);
        _uow.Setup(u => u.SmsAuditLogs).Returns(_audits.Object);
        _sut = new QueueSmsCommandHandler(_uow.Object, _notifier.Object, NullLogger<QueueSmsCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidInput_SavesSmsAndNotifies()
    {
        var cmd = new QueueSmsCommand { PhoneNumber = "0901234567", Message = "Hello", SourceService = "auth", CorrelationId = Guid.NewGuid(), Category = "otp" };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().NotBe(Guid.Empty);
        _msgs.Verify(m => m.AddAsync(It.Is<SmsMessage>(s => s.PhoneNumber == "+84901234567")), Times.Once);
        _audits.Verify(a => a.AddAsync(It.IsAny<SmsAuditLog>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyNewPendingSmsAsync(It.IsAny<Guid>(), "+84901234567", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidPhone_Returns400_WithFieldError()
    {
        var cmd = new QueueSmsCommand { PhoneNumber = "xyz", Message = "Hello", SourceService = "auth", CorrelationId = Guid.NewGuid() };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        // Field-level validation → ListErrors populated với Field + Detail.
        resp.ListErrors.Should().ContainSingle(e => e.Field == nameof(QueueSmsCommand.PhoneNumber));
        _msgs.Verify(m => m.AddAsync(It.IsAny<SmsMessage>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyMessage_Returns400_WithFieldError()
    {
        var cmd = new QueueSmsCommand { PhoneNumber = "0901234567", Message = "   ", SourceService = "auth", CorrelationId = Guid.NewGuid() };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().ContainSingle(e => e.Field == nameof(QueueSmsCommand.Message));
    }

    [Fact]
    public async Task Handle_MessageTooLong_Returns400_WithFieldError()
    {
        var longMsg = new string('a', 1601);
        var cmd = new QueueSmsCommand { PhoneNumber = "0901234567", Message = longMsg, SourceService = "auth", CorrelationId = Guid.NewGuid() };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().ContainSingle(e => e.Field == nameof(QueueSmsCommand.Message));
    }

    [Fact]
    public async Task Handle_InvalidPhoneAndEmptyMessage_GathersAllErrors_NotFailFast()
    {
        // Spec: collect ALL field errors instead of fail-fast on first one.
        var cmd = new QueueSmsCommand { PhoneNumber = "xyz", Message = "", SourceService = "auth", CorrelationId = Guid.NewGuid() };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().HaveCount(2);
        resp.ListErrors.Should().Contain(e => e.Field == nameof(QueueSmsCommand.PhoneNumber));
        resp.ListErrors.Should().Contain(e => e.Field == nameof(QueueSmsCommand.Message));
    }

    [Fact]
    public async Task Handle_NotifierFails_StillReturnsSuccess()
    {
        // SignalR fail không nên block — Flutter sẽ fallback polling.
        _notifier.Setup(n => n.NotifyNewPendingSmsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR down"));

        var cmd = new QueueSmsCommand { PhoneNumber = "0901234567", Message = "Hello", SourceService = "auth", CorrelationId = Guid.NewGuid() };

        var resp = await _sut.Handle(cmd, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
