using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Infrastructure.Implements.Services;

namespace SmsService.UnitTests.Services;

/// <summary>
/// <see cref="GatewaySmsProvider"/> (<c>NOTI3-05</c> / <c>#705</c>) — cầu nối giữa
/// <c>ISmsProvider</c> chung và hạ tầng gateway Android. Trước bộ test này phủ 0%.
///
/// <para><b>Ngữ nghĩa dễ hiểu nhầm nhất:</b> trả <c>true</c> chỉ có nghĩa "đã XẾP HÀNG được", KHÔNG
/// phải "đã gửi tới điện thoại người dùng". Tin nhắn thật do thiết bị gateway kéo về sau. Ai đọc
/// <c>true</c> thành "đã gửi" sẽ báo cáo sai cho người dùng — nên nghĩa này được chốt bằng test.</para>
/// </summary>
public class GatewaySmsProviderTests
{
    private static (GatewaySmsProvider provider, Mock<IMediator> mediator) Build(
        CommonResponse<Guid> response)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

        return (new GatewaySmsProvider(mediator.Object, NullLogger<GatewaySmsProvider>.Instance), mediator);
    }

    [Fact]
    public void ProviderName_IsStable()
    {
        var (provider, _) = Build(new CommonResponse<Guid> { IsSuccess = true });

        provider.ProviderName.Should().Be("android-gateway",
            "tên này đi vào log và metric — đổi là gãy dashboard đang có");
    }

    [Fact]
    public async Task SendAsync_WhenQueued_ReturnsTrue_AndForwardsEveryField()
    {
        var (provider, mediator) = Build(new CommonResponse<Guid> { IsSuccess = true, Data = Guid.NewGuid() });
        var correlation = Guid.NewGuid();

        var ok = await provider.SendAsync("0901234567", "noi dung", "TicketService", correlation);

        ok.Should().BeTrue();
        mediator.Verify(m => m.Send(
            It.Is<QueueSmsCommand>(c =>
                c.PhoneNumber == "0901234567" &&
                c.Message == "noi dung" &&
                c.SourceService == "TicketService" &&
                c.CorrelationId == correlation),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// <c>QueueSmsCommand.CorrelationId</c> không nhận null. Provider phải quy null về
    /// <see cref="Guid.Empty"/> thay vì ném — người gọi không phải lúc nào cũng có correlation.
    /// </summary>
    [Fact]
    public async Task SendAsync_WithoutCorrelation_UsesEmptyGuid()
    {
        var (provider, mediator) = Build(new CommonResponse<Guid> { IsSuccess = true });

        await provider.SendAsync("0901234567", "noi dung", "NotificationService");

        mediator.Verify(m => m.Send(
            It.Is<QueueSmsCommand>(c => c.CorrelationId == Guid.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Xếp hàng thất bại (vd số điện thoại sai định dạng, thiết bị hết hạn mức) phải trả
    /// <c>false</c> — KHÔNG được ném. Người gọi là các consumer thông báo; ném ở đây sẽ đẩy message
    /// vào hàng đợi lỗi vì một lý do nghiệp vụ hoàn toàn bình thường.
    /// </summary>
    [Fact]
    public async Task SendAsync_WhenQueueRejects_ReturnsFalse_WithoutThrowing()
    {
        var (provider, _) = Build(new CommonResponse<Guid>
        {
            IsSuccess = false,
            Message = "So dien thoai khong hop le",
        });

        var ok = await provider.SendAsync("khong-phai-so", "noi dung", "TicketService");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_PassesCancellationTokenThrough()
    {
        var (provider, mediator) = Build(new CommonResponse<Guid> { IsSuccess = true });
        using var cts = new CancellationTokenSource();

        await provider.SendAsync("0901234567", "noi dung", "TicketService", null, cts.Token);

        mediator.Verify(m => m.Send(It.IsAny<QueueSmsCommand>(), cts.Token), Times.Once);
    }
}
