using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Helper khởi tạo MassTransit test harness cho 1 consumer + mock <see cref="INotificationUnitOfWork"/>
/// capture mọi <see cref="Notification"/> được ghi (qua <c>Notifications.AddAsync</c>). Dùng chung
/// cho các consumer GH-107 (ghi notification trực tiếp qua UnitOfWork).
///
/// GH-604: đăng ký thêm mock <see cref="IRecipientResolver"/>. Mặc định resolve về 1 recipient
/// (<see cref="DefaultRecipient"/>) cho mọi role; truyền <paramref name="recipients"/> = danh sách rỗng
/// để test nhánh "không có recipient → skip".
///
/// <see cref="ICacheService"/> mặc định: <c>GetAsync</c> trả <c>null</c> (message chưa thấy → cho qua).
/// Truyền <paramref name="cache"/> để test nhánh debounce (duplicate message → skip).
/// </summary>
public static class ConsumerTestHarness
{
    /// <summary>Recipient mặc định resolver trả về — test assert UserId của notification ghi ra.</summary>
    public static readonly Guid DefaultRecipient = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    /// <summary>
    /// Trần chờ tổng của harness. Chỉ chạm tới khi thật sự hỏng, nên đặt rộng tay:
    /// test hỏng chậm 30 giây vẫn tốt hơn test xanh-đỏ thất thường.
    /// </summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ⚠️ ĐÂY LÀ THAM SỐ GÂY FLAKY — mặc định của MassTransit v8 chỉ **1 giây**.
    ///
    /// <c>harness.Consumed.Any&lt;T&gt;()</c> ngừng chờ khi bus "im" quá khoảng này rồi trả
    /// <c>false</c> — nghĩa là **hết giờ và thật sự hỏng cho ra CÙNG một kết quả**, không phân biệt được.
    /// Khi cả solution chạy song song (<c>make ci-test</c> bung ~9 assembly cùng lúc), việc điều phối
    /// luồng có thể trượt quá 1 giây và test đỏ dù code hoàn toàn đúng.
    ///
    /// Đo được ngày 31/07/2026: một lần <c>make ci-full</c> đỏ 6 test consumer, tất cả khởi động
    /// trong 0,3 giây cuối của run — trong khi test PASS chậm nhất cùng cửa sổ mất 3,21 giây, tức
    /// sát mép. Chạy riêng assembly này thì 107 test xong trong ~370ms và pass 5/5 lần.
    ///
    /// Nâng lên 15 giây: dư sức cho lúc máy nghẽn, mà vẫn đủ ngắn để test hỏng thật không treo lâu.
    /// </summary>
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(15);

    public static async Task<(ITestHarness harness, List<Notification> written, Mock<INotificationUnitOfWork> uow)> StartAsync<TConsumer>(
        IReadOnlyList<Guid>? recipients = null,
        ICacheService? cache = null)
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
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TConsumer>();
                x.SetTestTimeouts(TestTimeout, InactivityTimeout);
            })
            .AddSingleton(uow.Object)
            .AddSingleton(resolver.Object)
            .AddSingleton(cache ?? ProceedCache())
            .AddLogging()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, written, uow);
    }

    /// <summary>
    /// Cache mock mặc định: chiếm được key → message chưa thấy → debounce cho qua.
    /// Sprint 6.3 NOTI3-09 (#709): debounce nay dùng <c>TrySetIfNotExistsAsync</c> (SET NX atomic)
    /// thay cho cặp GetAsync/SetAsync, nên mock phải setup đúng method đó.
    /// </summary>
    public static ICacheService ProceedCache()
    {
        var c = new Mock<ICacheService>();
        c.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        c.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return c.Object;
    }

    /// <summary>
    /// Cache mock debounce: KHÔNG chiếm được key → message đã xử lý → consumer skip.
    /// Dùng để test nhánh duplicate message (MassTransit retry scenario).
    /// </summary>
    public static ICacheService AlreadySeenCache()
    {
        var c = new Mock<ICacheService>();
        c.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1");
        c.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return c.Object;
    }
}
