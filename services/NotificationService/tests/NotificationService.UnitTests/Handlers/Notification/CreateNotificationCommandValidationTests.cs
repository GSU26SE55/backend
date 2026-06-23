using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.Notification;

public class CreateNotificationCommandValidationTests
{
    private static CreateNotificationCommand Valid(Guid userId) => new()
    {
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Title = "Tiêu đề",
        Body = "Nội dung",
    };

    [Fact]
    public async Task EmptyUserId_PassesValidation()
    {
        // GH-594: Guid.Empty (broadcast placeholder) KHÔNG bị reject nữa.
        var cmd = Valid(Guid.Empty);

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task RealUserId_PassesValidation()
    {
        var resp = await Valid(Guid.NewGuid()).ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingTitle_StillFails()
    {
        var cmd = Valid(Guid.Empty);
        cmd.Title = "";

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "Title");
    }

    [Fact]
    public async Task InvalidType_StillFails()
    {
        var cmd = Valid(Guid.Empty);
        cmd.Type = (NotificationTypeEnum)9999;

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Type");
    }
}
