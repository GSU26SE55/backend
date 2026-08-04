using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.NotificationTemplate;

/// <summary>
/// Kiểm tầng <c>ValidateAsync</c> (chạy trong ValidationBehavior, trước khi vào handler).
/// Thu thập TẤT CẢ lỗi chứ không dừng ở lỗi đầu — người soạn sửa một lượt, không phải thử từng cái.
/// </summary>
public class NotificationTemplateValidationTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static NotificationTemplateCreateCommand ValidCreate() => new()
    {
        Type = NotificationTypeEnum.SlaWarning,
        Channel = NotificationChannelEnum.Email,
        TitleTemplate = "Tiêu đề",
        BodyTemplate = "Nội dung",
        ActorUserId = Actor,
    };

    [Fact]
    public async Task Create_HopLe_KhongLoi()
    {
        var result = await ValidCreate().ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ThieuNoiDung_BaoDuCaHaiLoiMotLuot()
    {
        var command = ValidCreate();
        command.TitleTemplate = "   ";
        command.BodyTemplate = "";

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Select(e => e.Field).Should().Contain(new[] { "TitleTemplate", "BodyTemplate" });
    }

    [Theory]
    [InlineData(500, 4000, true)]
    [InlineData(501, 4000, false)]
    [InlineData(500, 4001, false)]
    public async Task Create_GioiHanDoDaiKhopCotDB(int titleLength, int bodyLength, bool expectValid)
    {
        var command = ValidCreate();
        command.TitleTemplate = new string('a', titleLength);
        command.BodyTemplate = new string('b', bodyLength);

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().Be(expectValid);
    }

    [Fact]
    public async Task Create_EnumKhongHopLe_BaoLoi()
    {
        var command = ValidCreate();
        command.Type = (NotificationTypeEnum)9999;
        command.Channel = (NotificationChannelEnum)77;

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Select(e => e.Field).Should().Contain(new[] { "Type", "Channel" });
    }

    /// <summary>Actor rỗng ⇒ audit sẽ ghi với người thực hiện rỗng, không truy trách nhiệm được.</summary>
    [Fact]
    public async Task Create_ThieuActor_BaoLoi()
    {
        var command = ValidCreate();
        command.ActorUserId = Guid.Empty;

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ActorUserId");
    }

    [Fact]
    public async Task Revise_ThieuId_BaoLoi()
    {
        var result = await new NotificationTemplateReviseCommand
        {
            Id = Guid.Empty,
            TitleTemplate = "a",
            BodyTemplate = "b",
            ActorUserId = Actor,
        }.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Fact]
    public async Task Revise_KhongDoiHoiTypeChannel()
    {
        // Revise cố ý KHÔNG nhận Type/Channel — lấy từ bản gốc, nên validate không được đòi.
        var result = await new NotificationTemplateReviseCommand
        {
            Id = Guid.NewGuid(),
            TitleTemplate = "a",
            BodyTemplate = "b",
            ActorUserId = Actor,
        }.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(NotificationTemplateActivateCommand))]
    [InlineData(typeof(NotificationTemplateDeleteCommand))]
    public async Task ActivateVaDelete_ThieuIdHoacActor_BaoLoi(Type commandType)
    {
        dynamic command = Activator.CreateInstance(commandType)!;
        command.Id = Guid.Empty;
        command.ActorUserId = Guid.Empty;

        var result = await command.ValidateAsync();

        ((bool)result.IsSuccess).Should().BeFalse();
        ((int)result.StatusCode).Should().Be(400);
    }

    [Fact]
    public async Task TestSend_ThieuActor_BaoLoi()
    {
        var result = await new NotificationTemplateTestSendCommand
        {
            Id = Guid.NewGuid(),
            ActorUserId = Guid.Empty,
        }.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ActorUserId");
    }
}
