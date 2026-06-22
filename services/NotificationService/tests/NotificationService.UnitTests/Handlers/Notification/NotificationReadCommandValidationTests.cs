using NotificationService.Application.CQRS.Command.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

public class NotificationReadCommandValidationTests
{
    // ---------- MarkNotificationReadCommand ----------

    [Fact]
    public async Task MarkRead_Valid_PassesValidation()
    {
        var cmd = new MarkNotificationReadCommand { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkRead_EmptyId_Fails()
    {
        var cmd = new MarkNotificationReadCommand { Id = Guid.Empty, UserId = Guid.NewGuid() };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Fact]
    public async Task MarkRead_EmptyUserId_Fails()
    {
        var cmd = new MarkNotificationReadCommand { Id = Guid.NewGuid(), UserId = Guid.Empty };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    // ---------- MarkAllNotificationsReadCommand ----------

    [Fact]
    public async Task MarkAll_Valid_PassesValidation()
    {
        var cmd = new MarkAllNotificationsReadCommand { UserId = Guid.NewGuid() };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAll_EmptyUserId_Fails()
    {
        var cmd = new MarkAllNotificationsReadCommand { UserId = Guid.Empty };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "UserId");
    }
}
