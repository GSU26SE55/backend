using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.UnitTests.Handlers.Sms;

public class CancelSmsCommandHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsMessage>> _msgs = new();
    private readonly Mock<IGenericRepository<SmsAuditLog>> _audits = new();
    private readonly Mock<ISmsGatewayNotifier> _notifier = new();
    private readonly CancelSmsCommandHandler _sut;

    public CancelSmsCommandHandlerTests()
    {
        _uow.Setup(u => u.SmsMessages).Returns(_msgs.Object);
        _uow.Setup(u => u.SmsAuditLogs).Returns(_audits.Object);
        _sut = new CancelSmsCommandHandler(_uow.Object, _notifier.Object, NullLogger<CancelSmsCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        _msgs.Setup(m => m.GetAllAsync()).Returns(new List<SmsMessage>().BuildMock());

        var resp = await _sut.Handle(new CancelSmsCommand { SmsId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(SmsStatus.Sent)]
    [InlineData(SmsStatus.Failed)]
    [InlineData(SmsStatus.Cancelled)]
    public async Task Handle_TerminalState_Returns409(SmsStatus current)
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "x",
            Status = current,
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new CancelSmsCommand { SmsId = sms.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Theory]
    [InlineData(SmsStatus.Pending)]
    [InlineData(SmsStatus.Sending)]
    public async Task Handle_CancellableState_CancelsAndNotifies(SmsStatus current)
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "x",
            Status = current,
            GatewayDeviceCode = current == SmsStatus.Sending ? "device-A" : null,
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new CancelSmsCommand { SmsId = sms.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        sms.Status.Should().Be(SmsStatus.Cancelled);
        _notifier.Verify(n => n.NotifyBatchRevokedAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _audits.Verify(a => a.AddAsync(It.IsAny<SmsAuditLog>()), Times.Once);
    }
}
