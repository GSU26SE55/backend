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

    /// <summary>
    /// Việc huỷ đã được LƯU trước khi báo cho thiết bị. Nếu SignalR chết mà handler để lỗi bay lên,
    /// người dùng sẽ nhận 500 cho một thao tác đã thành công — rồi bấm huỷ lại và nhận 409
    /// "đã ở trạng thái Cancelled". Vì vậy lỗi thông báo chỉ được ghi log, không được ném.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNotifierThrows_StillSucceeds()
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "x",
            Status = SmsStatus.Sending,
            GatewayDeviceCode = "device-A",
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());
        _notifier
            .Setup(n => n.NotifyBatchRevokedAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hub không kết nối được"));

        var resp = await _sut.Handle(new CancelSmsCommand { SmsId = sms.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue("việc huỷ đã lưu xong — báo thiết bị hỏng không được lật ngược kết quả");
        resp.StatusCode.Should().Be(200);
        sms.Status.Should().Be(SmsStatus.Cancelled);
    }

    /// <summary>
    /// Tin chưa được thiết bị nào nhận thì <c>GatewayDeviceCode</c> rỗng — phải rơi về
    /// <c>TargetDeviceCode</c> để báo đúng máy đã được chỉ định, thay vì phát cho tất cả.
    /// </summary>
    [Fact]
    public async Task Handle_NotYetClaimed_NotifiesTargetDeviceCode()
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "x",
            Status = SmsStatus.Pending,
            GatewayDeviceCode = null,
            TargetDeviceCode = "device-duoc-chi-dinh",
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        await _sut.Handle(new CancelSmsCommand { SmsId = sms.Id }, CancellationToken.None);

        _notifier.Verify(n => n.NotifyBatchRevokedAsync(
            It.IsAny<IEnumerable<Guid>>(), "device-duoc-chi-dinh", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// <see cref="NullSmsGatewayNotifier"/> là bản cài đặt rỗng dùng khi chưa bật SignalR. Nó phải
    /// im lặng hoàn tất — ném ở đây sẽ làm hỏng mọi luồng huỷ trong môi trường không có realtime.
    /// </summary>
    [Fact]
    public async Task NullNotifier_DoesNothing_AndNeverThrows()
    {
        var notifier = new NullSmsGatewayNotifier();

        var act = async () =>
        {
            await notifier.NotifyNewPendingSmsAsync(Guid.NewGuid(), "+84901234567", "device-A");
            await notifier.NotifyBatchRevokedAsync(new[] { Guid.NewGuid() }, null);
        };

        await act.Should().NotThrowAsync();
    }
}
