using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Application.CQRS.Handler.DeviceToken;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;

namespace NotificationService.UnitTests.Handlers.DeviceToken;

public class RegisterDeviceTokenCommandHandlerTests
{
    private static RegisterDeviceTokenCommand MakeCommand(Guid userId, string token = "ExponentPushToken[abc]") => new()
    {
        UserId = userId,
        Token = token,
        Platform = DevicePlatformEnum.Android,
        DeviceInfo = "Pixel 8 / Android 14"
    };

    [Fact]
    public async Task Register_NewToken_Creates_Returns201()
    {
        var userId = Guid.NewGuid();
        var (uow, deviceTokens, _) = MockNotificationUnitOfWork.Build();
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(MakeCommand(userId), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(201);
        resp.Data.Should().NotBe(Guid.Empty);
        deviceTokens.Verify(r => r.AddAsync(It.Is<DeviceTokenEntity>(
            x => x.UserId == userId && x.IsActive && x.Token == "ExponentPushToken[abc]")), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_ActiveTokenSameUser_Returns409_NoWrite()
    {
        var userId = Guid.NewGuid();
        var existing = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "ExponentPushToken[abc]",
            Platform = DevicePlatformEnum.Android,
            IsActive = true
        };
        var (uow, deviceTokens, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: new[] { existing });
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(MakeCommand(userId), CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(409);
        resp.Data.Should().Be(existing.Id);
        deviceTokens.Verify(r => r.AddAsync(It.IsAny<DeviceTokenEntity>()), Times.Never);
        deviceTokens.Verify(r => r.UpdateAsync(It.IsAny<DeviceTokenEntity>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_InactiveToken_ReactivatesForCurrentUser_Returns200()
    {
        var userId = Guid.NewGuid();
        var existing = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(), // tài khoản cũ
            Token = "ExponentPushToken[abc]",
            Platform = DevicePlatformEnum.Ios,
            IsActive = false
        };
        var (uow, deviceTokens, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: new[] { existing });
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(MakeCommand(userId), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(existing.Id);
        existing.IsActive.Should().BeTrue();
        existing.UserId.Should().Be(userId);
        existing.Platform.Should().Be(DevicePlatformEnum.Android);
        deviceTokens.Verify(r => r.AddAsync(It.IsAny<DeviceTokenEntity>()), Times.Never);
        deviceTokens.Verify(r => r.UpdateAsync(existing), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_ActiveTokenDifferentUser_Reassigns_Returns200()
    {
        var newUser = Guid.NewGuid();
        var existing = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = "ExponentPushToken[abc]",
            Platform = DevicePlatformEnum.Ios,
            IsActive = true
        };
        var (uow, _, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: new[] { existing });
        var handler = new RegisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(MakeCommand(newUser), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        existing.UserId.Should().Be(newUser);
        existing.IsActive.Should().BeTrue();
    }
}
