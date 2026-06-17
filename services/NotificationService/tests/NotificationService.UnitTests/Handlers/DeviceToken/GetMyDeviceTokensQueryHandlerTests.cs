using NotificationService.Application.CQRS.Handler.DeviceToken;
using NotificationService.Application.CQRS.Query.DeviceToken;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;

namespace NotificationService.UnitTests.Handlers.DeviceToken;

public class GetMyDeviceTokensQueryHandlerTests
{
    [Fact]
    public async Task Get_ReturnsOnlyCurrentUserNonDeletedDevices()
    {
        var userId = Guid.NewGuid();
        var seed = new[]
        {
            new DeviceTokenEntity { Id = Guid.NewGuid(), UserId = userId, Token = "t1", Platform = DevicePlatformEnum.Android, IsActive = true },
            new DeviceTokenEntity { Id = Guid.NewGuid(), UserId = userId, Token = "t2", Platform = DevicePlatformEnum.Ios, IsActive = false },
            new DeviceTokenEntity { Id = Guid.NewGuid(), UserId = userId, Token = "t3", Platform = DevicePlatformEnum.Web, IsActive = true, IsDeleted = true },
            new DeviceTokenEntity { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Token = "t4", Platform = DevicePlatformEnum.Android, IsActive = true },
        };
        var (uow, _, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: seed);
        var handler = new GetMyDeviceTokensQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetMyDeviceTokensQuery { UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().HaveCount(2); // loại deleted (t3) và device user khác (t4)
        resp.Data!.Select(d => d.Id).Should().Contain(new[] { seed[0].Id, seed[1].Id });
    }

    [Fact]
    public async Task Get_NoDevices_ReturnsEmptyList()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var handler = new GetMyDeviceTokensQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetMyDeviceTokensQuery { UserId = Guid.NewGuid() }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().BeEmpty();
    }
}
