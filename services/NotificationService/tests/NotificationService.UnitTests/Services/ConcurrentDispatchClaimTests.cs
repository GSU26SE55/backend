using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// GH-793 — khi hai replica cùng nhặt một bản ghi, chỉ một bên được gửi.
/// </summary>
/// <remarks>
/// <para>
/// Quyền chạy độc quyền (leader lease) thu hẹp khả năng đó nhưng không loại bỏ được: lease hết hạn
/// giữa lượt chạy dài, Redis sự cố (lúc đó mã cố ý chạy tiếp để không ai bị bỏ quên), hay đồng hồ
/// lệch đều dẫn tới hai chủ. Vì vậy hàng rào cuối cùng phải nằm ở cơ sở dữ liệu.
/// </para>
/// <para>
/// Bên thua KHÔNG được coi là lỗi: bản ghi vẫn đang được bên kia xử lý bình thường.
/// </para>
/// </remarks>
public class ConcurrentDispatchClaimTests
{
    private static readonly Guid UserId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private static Notification Pending() => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = NotificationStatusEnum.Pending,
        Title = "Title",
        Body = "Content",
        EntityType = "Ticket",
    };

    private static (NotificationDispatcher Sut, Mock<INotificationUnitOfWork> Uow, Mock<INotificationChannel> Channel)
        Build(Notification notification)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: [],
            notificationSeed: [notification],
            accountSeed:
            [
                new AccountReadModel
                {
                    Id = UserId, Email = "user@x.com", FullName = "User",
                    PhoneNumber = "0901234567", Role = "Customer", IsActive = true,
                }
            ],
            templateSeed: [],
            batchSeed: []);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<NotificationPreference>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((NotificationPreference?)null);

        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(true));

        var sut = new NotificationDispatcher(
            uow.Object,
            cache.Object,
            [channel.Object],
            new Mock<ITemplateRenderer>().Object,
            new NoopAuditWriter(),
            Microsoft.Extensions.Options.Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        return (sut, uow, channel);
    }

    [Fact]
    public async Task LosingTheClaim_MeansNoExternalSend()
    {
        // Đây là hàng rào cuối: dù đã lọt qua mọi tầng phía trên, không chiếm được bản ghi thì KHÔNG
        // được gọi ra ngoài. Thiếu khẳng định này, cả thiết kế chỉ dựa vào lease Redis.
        var n = Pending();
        var (sut, uow, channel) = Build(n);
        uow.Setup(u => u.TryClaimForDispatchAsync(
               It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(false);

        var outcome = await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        outcome.Should().Be(DispatchOutcome.Deferred, "thua tranh chấp không phải lỗi — bên kia đang lo");
    }

    [Fact]
    public async Task LosingTheClaim_DoesNotTouchTheRecord()
    {
        // Bên thua ghi đè trạng thái sẽ phá đúng bản ghi mà bên thắng đang xử lý.
        var n = Pending();
        var (sut, uow, _) = Build(n);
        uow.Setup(u => u.TryClaimForDispatchAsync(
               It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(false);

        await sut.DispatchPendingAsync(n);

        n.Status.Should().Be(NotificationStatusEnum.Pending);
        n.DispatchAttemptCount.Should().Be(0);
        n.ProcessingStartedAt.Should().BeNull();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TwoRacingDispatchers_ProduceExactlyOneSend()
    {
        // Mô phỏng trọng tài của cơ sở dữ liệu: đúng một lời gọi chiếm việc nhận được true, y như
        // câu `UPDATE … WHERE Status = Pending` chỉ ảnh hưởng 1 dòng đúng một lần.
        var n = Pending();
        var claimsGranted = 0;

        var sends = 0;
        for (var replica = 0; replica < 2; replica++)
        {
            var (sut, uow, channel) = Build(n);
            uow.Setup(u => u.TryClaimForDispatchAsync(
                   It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => Interlocked.Increment(ref claimsGranted) == 1);
            channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
                   .Callback(() => Interlocked.Increment(ref sends))
                   .ReturnsAsync(new ChannelResult(true));

            await sut.DispatchPendingAsync(n);

            // Replica thứ hai đọc bản ghi từ trước khi bên kia chiếm — đúng như đọc DB chậm một nhịp.
            n.Status = NotificationStatusEnum.Pending;
        }

        sends.Should().Be(1, "hai replica cùng nhặt một bản ghi chỉ được gửi đúng một lần");
    }

    [Fact]
    public async Task ClaimUsesTheRecordId_NotSomethingElse()
    {
        // Chiếm nhầm khoá (ví dụ UserId) sẽ khoá cả những bản ghi khác của cùng người dùng, hoặc tệ
        // hơn là chẳng khoá gì cả.
        var n = Pending();
        var (sut, uow, _) = Build(n);
        Guid claimedId = Guid.Empty;
        uow.Setup(u => u.TryClaimForDispatchAsync(
               It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
           .Callback<Guid, DateTime, CancellationToken>((id, _, _) => claimedId = id)
           .ReturnsAsync(true);

        await sut.DispatchPendingAsync(n);

        claimedId.Should().Be(n.Id);
    }
}
