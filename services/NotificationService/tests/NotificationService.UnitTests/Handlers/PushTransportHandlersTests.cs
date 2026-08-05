using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.CQRS.Command.Setting;
using NotificationService.Application.CQRS.Handler.Setting;
using NotificationService.Application.CQRS.Query.Setting;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// ADR-0019 — API cho màn hình Admin đổi đường vận chuyển push.
/// </summary>
public class PushTransportHandlersTests
{
    private static Mock<IPushTransportSettingService> Setting(PushTransportEnum current)
    {
        var m = new Mock<IPushTransportSettingService>();
        m.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(current);
        return m;
    }

    // ════════════════════════ GET ════════════════════════

    [Fact]
    public async Task Get_TraVeGiaTriHienTai()
    {
        var handler = new GetPushTransportQueryHandler(Setting(PushTransportEnum.Both).Object);

        var response = await handler.Handle(new GetPushTransportQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Data!.Transport.Should().Be(PushTransportEnum.Both);
        response.Data.TransportName.Should().Be("Both");
    }

    [Fact]
    public async Task Get_TraVeDuMoiLuaChonHopLe_DeFrontendKhongPhaiHardCode()
    {
        var handler = new GetPushTransportQueryHandler(Setting(PushTransportEnum.SignalR).Object);

        var response = await handler.Handle(new GetPushTransportQuery(), CancellationToken.None);

        var options = response.Data!.Options;

        // Đủ đúng bằng số phần tử của enum: thêm transport mới mà quên bổ sung mô tả thì test đỏ,
        // thay vì giao diện âm thầm thiếu một lựa chọn.
        options.Should().HaveCount(Enum.GetValues<PushTransportEnum>().Length);
        options.Select(o => o.Value).Should().BeEquivalentTo(Enum.GetValues<PushTransportEnum>());
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Description));
        options.Single(o => o.Value == PushTransportEnum.Expo).RequiresDeviceToken.Should().BeTrue();
        options.Single(o => o.Value == PushTransportEnum.SignalR).RequiresDeviceToken.Should().BeFalse();
    }

    // ════════════════════════ PUT ════════════════════════

    [Fact]
    public async Task Update_LuuGiaTriMoiVaTraVeNo()
    {
        var setting = Setting(PushTransportEnum.SignalR);
        var handler = new UpdatePushTransportCommandHandler(
            setting.Object, NullLogger<UpdatePushTransportCommandHandler>.Instance);

        var response = await handler.Handle(
            new UpdatePushTransportCommand { Transport = PushTransportEnum.Both }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Transport.Should().Be(PushTransportEnum.Both);
        setting.Verify(x => x.SetAsync(PushTransportEnum.Both, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_GiaTriTrungGiaTriCu_KhongGhiLai()
    {
        var setting = Setting(PushTransportEnum.Expo);
        var handler = new UpdatePushTransportCommandHandler(
            setting.Object, NullLogger<UpdatePushTransportCommandHandler>.Instance);

        var response = await handler.Handle(
            new UpdatePushTransportCommand { Transport = PushTransportEnum.Expo }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        setting.Verify(x => x.SetAsync(It.IsAny<PushTransportEnum>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════ Validate ════════════════════════

    [Theory]
    [InlineData(PushTransportEnum.SignalR)]
    [InlineData(PushTransportEnum.Expo)]
    [InlineData(PushTransportEnum.Both)]
    public async Task Validate_GiaTriHopLe_ThiQua(PushTransportEnum transport)
    {
        var response = await new UpdatePushTransportCommand { Transport = transport }.ValidateAsync();

        response.IsSuccess.Should().BeTrue();
        response.ListErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]   // trường thiếu trong body → giá trị mặc định của enum
    [InlineData(99)]
    [InlineData(-1)]
    public async Task Validate_GiaTriNgoaiDai_ThiChan(int raw)
    {
        var response = await new UpdatePushTransportCommand { Transport = (PushTransportEnum)raw }.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().ContainSingle(e => e.Field == "Transport");
    }
}
