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
/// GH-792 — bản ghi phải được CHIẾM và ghi xuống DB TRƯỚC khi gọi provider.
/// </summary>
/// <remarks>
/// <para>
/// Trước đây thứ tự là: gọi provider → mới ghi <c>Sent</c>. Giữa hai bước đó có một cửa sổ mà tiến
/// trình chết hoặc DB ghi hỏng sẽ để lại bản ghi ở nguyên <c>Pending</c> — dù email/SMS/push đã thật
/// sự rời đi. Vòng quét sau nhặt lại và gửi lần thứ hai; không có cách nào đối soát vì DB chưa bao
/// giờ biết lần gửi đầu đã xảy ra.
/// </para>
/// <para>
/// Test ở đây soi đúng bất biến đó: <b>tại thời điểm provider được gọi</b>, bản ghi đã là
/// <c>Processing</c> và đã có ít nhất một lần ghi DB. Kiểm sau khi hàm chạy xong là không đủ —
/// trạng thái cuối cùng giống hệt nhau ở cả hai thứ tự.
/// </para>
/// </remarks>
public class DispatchClaimBeforeSendTests
{
    private static readonly Guid UserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private static Mock<ICacheService> NoCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<NotificationPreference>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((NotificationPreference?)null);
        return m;
    }

    private static Notification Pending(int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = NotificationStatusEnum.Pending,
        Title = "Title",
        Body = "Content",
        EntityType = "Ticket",
        DispatchAttemptCount = attempts,
    };

    private static AccountReadModel Account() => new()
    {
        Id = UserId,
        Email = "user@x.com",
        FullName = "User",
        PhoneNumber = "0901234567",
        Role = "Customer",
        IsActive = true,
    };

    private static (NotificationDispatcher Sut, Mock<INotificationUnitOfWork> Uow) Build(
        Notification notification,
        INotificationChannel channel,
        NotificationDispatchOptions? options = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: [],
            notificationSeed: [notification],
            accountSeed: [Account()],
            templateSeed: [],
            batchSeed: []);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var sut = new NotificationDispatcher(
            uow.Object,
            NoCache().Object,
            [channel],
            new Mock<ITemplateRenderer>().Object,
            new NoopAuditWriter(),
            Microsoft.Extensions.Options.Options.Create(options ?? new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        return (sut, uow);
    }

    [Fact]
    public async Task ClaimIsPersisted_BeforeTheProviderIsCalled()
    {
        var n = Pending();
        var observations = new List<(NotificationStatusEnum Status, int Attempts, bool Claimed)>();
        var claimed = false;

        // Channel ghi lại trạng thái bản ghi ĐÚNG LÚC provider được gọi. Kiểm sau khi hàm chạy xong
        // là vô nghĩa: trạng thái cuối giống hệt nhau dù chiếm trước hay chiếm sau.
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .Callback(() => observations.Add((n.Status, n.DispatchAttemptCount, claimed)))
               .ReturnsAsync(new ChannelResult(true));

        var (sut, uow) = Build(n, channel.Object);
        uow.Setup(u => u.TryClaimForDispatchAsync(
               It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
           .Callback(() => claimed = true)
           .ReturnsAsync(true);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        observations.Should().ContainSingle("provider phải được gọi đúng một lần");

        var (statusAtSend, attemptsAtSend, claimedAtSend) = observations[0];
        statusAtSend.Should().Be(NotificationStatusEnum.Processing,
            "bản ghi phải rời khỏi hàng đợi TRƯỚC khi có tác động ra ngoài");
        attemptsAtSend.Should().Be(1, "một lần chiếm việc là một lần thử, kể cả khi sau đó chết");
        claimedAtSend.Should().BeTrue(
            "việc chiếm phải đã ghi xuống DB (một câu UPDATE có điều kiện), không chỉ đổi trong bộ nhớ");
    }

    [Fact]
    public async Task SuccessfulSend_EndsAtSent_WithoutDoubleCountingAttempts()
    {
        var n = Pending();
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(true));

        var (sut, _) = Build(n, channel.Object);

        await sut.DispatchPendingAsync(n);

        n.Status.Should().Be(NotificationStatusEnum.Sent);
        n.ProcessingStartedAt.Should().BeNull("việc đã xong thì không còn đang chiếm");
        n.DispatchAttemptCount.Should().Be(1, "đếm ở cả lúc chiếm lẫn lúc xong là đếm đôi");
    }

    [Fact]
    public async Task FailedSend_ReturnsToQueue_NotStuckInProcessing()
    {
        // Provider từ chối là chuyện bình thường — bản ghi phải quay lại hàng đợi. Quên trả về
        // Pending thì nó nằm lại Processing và chỉ bộ thu hồi mới cứu được, chậm hơn hẳn.
        var n = Pending();
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(false, "provider tu choi"));

        var (sut, _) = Build(n, channel.Object, new NotificationDispatchOptions { MaxAttempts = 5 });

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Retrying);
        n.Status.Should().Be(NotificationStatusEnum.Pending);
        n.ProcessingStartedAt.Should().BeNull();
        n.NextAttemptAt.Should().NotBeNull("phải có backoff, không thử lại ngay lập tức");
        n.DispatchAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedFailures_ReachMaxAttempts_AndStop()
    {
        // Đếm số lần thử ở lúc chiếm việc giúp một sự cố lặp lại vẫn tiến dần tới trần, thay vì quay
        // vòng mãi mà số đếm không nhích.
        var n = Pending(attempts: 4);
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(false, "provider tu choi"));

        var (sut, _) = Build(n, channel.Object, new NotificationDispatchOptions { MaxAttempts = 5 });

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        n.Status.Should().Be(NotificationStatusEnum.Failed);
        n.ProcessingStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task AlreadyProcessingRecord_IsNotSentAgain()
    {
        // Đây là dấu vết một tiến trình đã chết giữa chừng để lại. Gửi lại ngay lập tức chính là
        // hành vi sinh ra bản trùng mà issue mô tả — phải để bộ thu hồi quyết định, sau ngưỡng thời gian.
        var n = Pending();
        n.Status = NotificationStatusEnum.Processing;
        n.ProcessingStartedAt = DateTime.UtcNow;

        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(true));

        var (sut, _) = Build(n, channel.Object);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeferredRecord_IsNeverClaimed()
    {
        // Hoãn (quiet hours/digest) xảy ra TRƯỚC khi chiếm việc. Chiếm rồi mới hoãn sẽ để lại một
        // bản ghi Processing không ai gửi, và bộ thu hồi phải dọn — thêm một vòng chậm vô ích.
        var n = Pending();
        n.Channel = NotificationChannelEnum.Email;
        n.UserId = Guid.Empty;   // placeholder broadcast chưa resolve → dừng sớm

        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Email);

        var (sut, _) = Build(n, channel.Object);

        await sut.DispatchPendingAsync(n);

        n.Status.Should().NotBe(NotificationStatusEnum.Processing);
        n.ProcessingStartedAt.Should().BeNull();
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
