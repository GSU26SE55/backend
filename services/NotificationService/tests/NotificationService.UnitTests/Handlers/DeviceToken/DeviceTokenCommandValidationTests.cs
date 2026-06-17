using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.DeviceToken;

public class DeviceTokenCommandValidationTests
{
    // ---------- Register ----------

    [Fact]
    public async Task Register_Valid_PassesValidation()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = "ExponentPushToken[abc]",
            Platform = DevicePlatformEnum.Android,
            DeviceInfo = "Pixel 8"
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_EmptyUserId_Fails()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.Empty,
            Token = "t",
            Platform = DevicePlatformEnum.Ios
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task Register_EmptyToken_Fails()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = "   ",
            Platform = DevicePlatformEnum.Ios
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Token");
    }

    [Fact]
    public async Task Register_TokenTooLong_Fails()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = new string('x', 501),
            Platform = DevicePlatformEnum.Ios
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Token");
    }

    [Fact]
    public async Task Register_InvalidPlatform_Fails()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = "t",
            Platform = (DevicePlatformEnum)99
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Platform");
    }

    [Fact]
    public async Task Register_DeviceInfoTooLong_Fails()
    {
        var cmd = new RegisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = "t",
            Platform = DevicePlatformEnum.Web,
            DeviceInfo = new string('x', 501)
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "DeviceInfo");
    }

    // ---------- Unregister ----------

    [Fact]
    public async Task Unregister_Valid_PassesValidation()
    {
        var cmd = new UnregisterDeviceTokenCommand
        {
            UserId = Guid.NewGuid(),
            Token = "ExponentPushToken[abc]"
        };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Unregister_EmptyUserId_Fails()
    {
        var cmd = new UnregisterDeviceTokenCommand { UserId = Guid.Empty, Token = "t" };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task Unregister_EmptyToken_Fails()
    {
        var cmd = new UnregisterDeviceTokenCommand { UserId = Guid.NewGuid(), Token = "" };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Token");
    }

    [Fact]
    public async Task Unregister_TokenTooLong_Fails()
    {
        var cmd = new UnregisterDeviceTokenCommand { UserId = Guid.NewGuid(), Token = new string('x', 501) };

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Token");
    }
}
