using NotificationService.Application.CQRS.Command.DeviceToken;
using NotificationService.Application.CQRS.Handler.DeviceToken;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;

namespace NotificationService.UnitTests.Handlers.DeviceToken;

public class UnregisterDeviceTokenCommandHandlerTests
{
    [Fact]
    public async Task Unregister_OwnActiveToken_Deactivates_Returns200()
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
        var handler = new UnregisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new UnregisterDeviceTokenCommand { UserId = userId, Token = "ExponentPushToken[abc]" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        existing.IsActive.Should().BeFalse();
        deviceTokens.Verify(r => r.UpdateAsync(existing), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unregister_TokenNotFound_Returns404_NoWrite()
    {
        var (uow, deviceTokens, _) = MockNotificationUnitOfWork.Build();
        var handler = new UnregisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new UnregisterDeviceTokenCommand { UserId = Guid.NewGuid(), Token = "ExponentPushToken[missing]" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
        deviceTokens.Verify(r => r.UpdateAsync(It.IsAny<DeviceTokenEntity>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unregister_TokenOfAnotherUser_Returns404()
    {
        var existing = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(), // chủ khác
            Token = "ExponentPushToken[abc]",
            Platform = DevicePlatformEnum.Android,
            IsActive = true
        };
        var (uow, _, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: new[] { existing });
        var handler = new UnregisterDeviceTokenCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new UnregisterDeviceTokenCommand { UserId = Guid.NewGuid(), Token = "ExponentPushToken[abc]" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
        existing.IsActive.Should().BeTrue();
    }
}
