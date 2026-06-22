using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Helper khởi tạo MassTransit test harness cho 1 consumer + mock <see cref="INotificationUnitOfWork"/>
/// capture mọi <see cref="Notification"/> được ghi (qua <c>Notifications.AddAsync</c>). Dùng chung
/// cho các consumer GH-107 (ghi notification trực tiếp qua UnitOfWork).
/// </summary>
public static class ConsumerTestHarness
{
    public static async Task<(ITestHarness harness, List<Notification> written, Mock<INotificationUnitOfWork> uow)> StartAsync<TConsumer>()
        where TConsumer : class, IConsumer
    {
        var written = new List<Notification>();

        var repo = new Mock<IGenericRepository<Notification>>();
        repo.Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(written.Add)
            .Returns(Task.CompletedTask);

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.Notifications).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<TConsumer>())
            .AddSingleton(uow.Object)
            .AddLogging()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, written, uow);
    }
}
