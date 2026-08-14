using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.UnitTests.Helpers;

namespace NotificationService.UnitTests.Channels;

/// <summary>
/// ADR-0019 — kênh Push gộp: rẽ sang SignalR / Expo / cả hai theo cấu hình đổi được lúc chạy.
/// </summary>
public class CompositePushChannelTests
{
    private static readonly Guid UserId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private static Mock<ISignalRPushChannel> SignalR(bool success = true, string? error = null)
    {
        var m = new Mock<ISignalRPushChannel>();
        m.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        m.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new ChannelResult(success, error));
        return m;
    }

    private static Mock<IExpoPushChannel> Expo(bool success = true, string? error = null)
    {
        var m = new Mock<IExpoPushChannel>();
        m.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        m.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new ChannelResult(success, error));
        return m;
    }

    private static Mock<IPushTransportSettingService> Transport(PushTransportEnum transport)
    {
        var m = new Mock<IPushTransportSettingService>();
        m.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transport);
        return m;
    }

    private static DeviceToken Token(string value) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Token = value,
        Platform = DevicePlatformEnum.Android,
        IsActive = true,
    };

    private static CompositePushChannel Build(
        Mock<ISignalRPushChannel> signalR,
        Mock<IExpoPushChannel> expo,
        PushTransportEnum transport,
        DeviceToken[]? tokens = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: tokens ?? []);

        return new CompositePushChannel(
            signalR.Object,
            expo.Object,
            Transport(transport).Object,
            uow.Object,
            NullLogger<CompositePushChannel>.Instance);
    }

    private static SendRequest Request() => new()
    {
        NotificationId = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationTypeEnum.ChatCreated,
        Title = "Title",
        Body = "Content",
        CreatedAt = DateTime.UtcNow,
    };

    private static void VerifySent(Mock<ISignalRPushChannel> m, Times times) =>
        m.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), times);

    private static void VerifySent(Mock<IExpoPushChannel> m, Times times) =>
        m.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), times);

    // ════════════════════════ Rẽ nhánh theo transport ════════════════════════

    [Fact]
    public async Task SignalR_ChiGuiQuaHub_KhongDungExpo()
    {
        var signalR = SignalR();
        var expo = Expo();
        var sut = Build(signalR, expo, PushTransportEnum.SignalR, [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
        VerifySent(signalR, Times.Once());
        VerifySent(expo, Times.Never());
    }

    [Fact]
    public async Task Expo_ChiGuiQuaExpo_KhongDungHub()
    {
        var signalR = SignalR();
        var expo = Expo();
        var sut = Build(signalR, expo, PushTransportEnum.Expo, [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
        VerifySent(signalR, Times.Never());
        VerifySent(expo, Times.Once());
    }

    [Fact]
    public async Task Both_GuiCaHaiDuong()
    {
        var signalR = SignalR();
        var expo = Expo();
        var sut = Build(signalR, expo, PushTransportEnum.Both, [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
        VerifySent(signalR, Times.Once());
        VerifySent(expo, Times.Once());
    }

    // ════════════════════════ Device token ════════════════════════

    [Fact]
    public async Task Expo_NhanDuocToanBoTokenDangHoatDong()
    {
        SendRequest? captured = null;
        var expo = Expo();
        expo.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new ChannelResult(true));

        var sut = Build(SignalR(), expo, PushTransportEnum.Expo,
            [Token("ExponentPushToken[a]"), Token("ExponentPushToken[b]")]);

        await sut.SendAsync(Request());

        captured.Should().NotBeNull();
        captured!.ExpoTokens.Should().BeEquivalentTo(new[] { "ExponentPushToken[a]", "ExponentPushToken[b]" });
        captured.ExpoToken.Should().NotBeNull();
    }

    [Fact]
    public async Task Expo_KhongCoToken_BaoThatBaiVoiLyDoRoRang()
    {
        var expo = Expo();
        var sut = Build(SignalR(), expo, PushTransportEnum.Expo, tokens: []);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("device token");

        // Không gọi Expo khi chắc chắn không có gì để gửi tới.
        VerifySent(expo, Times.Never());
    }

    [Fact]
    public async Task Both_KhongCoToken_VanThanhCongNhoSignalR()
    {
        // Người dùng chỉ xài web thì không có device token nào — đó là chuyện bình thường ở chế độ
        // Both, không phải lỗi. Coi là thất bại sẽ làm dispatcher retry rồi đánh Failed một thông
        // báo mà thực tế đã tới nơi.
        var signalR = SignalR();
        var expo = Expo();
        var sut = Build(signalR, expo, PushTransportEnum.Both, tokens: []);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
        VerifySent(signalR, Times.Once());
        VerifySent(expo, Times.Never());
    }

    [Fact]
    public async Task Expo_BoQuaTokenDaTatVaDaXoaMem()
    {
        var inactive = Token("ExponentPushToken[inactive]");
        inactive.IsActive = false;
        var deleted = Token("ExponentPushToken[deleted]");
        deleted.IsDeleted = true;
        var good = Token("ExponentPushToken[good]");

        SendRequest? captured = null;
        var expo = Expo();
        expo.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new ChannelResult(true));

        var sut = Build(SignalR(), expo, PushTransportEnum.Expo, [inactive, deleted, good]);

        await sut.SendAsync(Request());

        captured!.ExpoTokens.Should().BeEquivalentTo(new[] { "ExponentPushToken[good]" });
    }

    // ════════════════════════ Gộp kết quả ════════════════════════

    [Fact]
    public async Task Both_MotDuongHong_VanTinhLaThanhCong()
    {
        var sut = Build(SignalR(), Expo(success: false, error: "Expo 500"), PushTransportEnum.Both,
            [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Both_CaHaiDuongHong_BaoThatBaiKemLyDoCuaTungDuong()
    {
        var sut = Build(
            SignalR(success: false, error: "hub down"),
            Expo(success: false, error: "Expo 500"),
            PushTransportEnum.Both,
            [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SignalR: hub down");
        result.ErrorMessage.Should().Contain("Expo: Expo 500");
    }

    [Fact]
    public async Task Both_MotDuongNemException_DuongConLaiVanChay()
    {
        // Dispatcher có lớp bắt exception riêng nhưng nó bọc CẢ cụm — nếu không bắt ở đây thì
        // SignalR hỏng sẽ cướp luôn cơ hội chạy của Expo.
        var signalR = new Mock<ISignalRPushChannel>();
        signalR.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        signalR.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("hub crashed"));

        var expo = Expo();
        var sut = Build(signalR, expo, PushTransportEnum.Both, [Token("ExponentPushToken[a]")]);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeTrue();
        VerifySent(expo, Times.Once());
    }

    [Fact]
    public async Task SignalR_NemException_TraVeThatBaiChuKhongNemLen()
    {
        var signalR = new Mock<ISignalRPushChannel>();
        signalR.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        signalR.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("hub crashed"));

        var sut = Build(signalR, Expo(), PushTransportEnum.SignalR);

        var result = await sut.SendAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("hub crashed");
    }

    [Fact]
    public void ChannelType_LaPush_DeDispatcherNhanRa()
    {
        var sut = Build(SignalR(), Expo(), PushTransportEnum.SignalR);

        sut.ChannelType.Should().Be(NotificationChannelEnum.Push);
    }
}
