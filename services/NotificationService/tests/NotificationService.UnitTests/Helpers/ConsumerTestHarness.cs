using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Helper khởi tạo MassTransit test harness cho 1 consumer + mock <see cref="INotificationUnitOfWork"/>
/// capture mọi <see cref="Notification"/> được ghi (qua <c>Notifications.AddAsync</c>). Dùng chung
/// cho các consumer GH-107 (ghi notification trực tiếp qua UnitOfWork).
///
/// GH-604: đăng ký thêm mock <see cref="IRecipientResolver"/>. Mặc định resolve về 1 recipient
/// (<see cref="DefaultRecipient"/>) cho mọi role; truyền <paramref name="recipients"/> = danh sách rỗng
/// để test nhánh "không có recipient → skip". Consumer không dùng resolver (recipient trực tiếp)
/// vẫn chạy bình thường vì registration thừa là vô hại.
/// </summary>
public static class ConsumerTestHarness
{
    /// <summary>Recipient mặc định resolver trả về — test assert UserId của notification ghi ra.</summary>
    public static readonly Guid DefaultRecipient = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    public static async Task<(ITestHarness harness, List<Notification> written, Mock<INotificationUnitOfWork> uow)> StartAsync<TConsumer>(
        IReadOnlyList<Guid>? recipients = null)
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

        var resolved = recipients ?? new[] { DefaultRecipient };
        var resolver = new Mock<IRecipientResolver>();
        resolver.Setup(r => r.GetActiveByRoleAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
            .ReturnsAsync(resolved);

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<TConsumer>())
            .AddSingleton(uow.Object)
            .AddSingleton(resolver.Object)
            .AddLogging()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, written, uow);
    }
}
