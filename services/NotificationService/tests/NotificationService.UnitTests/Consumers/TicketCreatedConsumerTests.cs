using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

public class TicketCreatedConsumerTests
{
    [Fact]
    public async Task TicketCreated_Writes_InAppPush_ResolvedRecipient()
    {
        var (harness, written, uow) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>();
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-001", Guid.NewGuid(), "P2High");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketCreated);
            n.UserId.Should().Be(ConsumerTestHarness.DefaultRecipient);
            n.EntityType.Should().Be("Ticket");
            n.EntityId.Should().Be(evt.TicketId);
            n.Title.Should().Contain("TKT-001");
            n.PayloadJson.Should().Contain("TicketDetail");
        });
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }

    [Fact]
    public async Task TicketCreated_NoRecipientResolved_SkipsWithoutWriting()
    {
        // Resolver trả rỗng (chưa có Manager nào trong read-model) → consumer skip, không ghi notification.
        var (harness, written, uow) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(Array.Empty<Guid>());
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-002", Guid.NewGuid(), "P2High");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task TicketCreated_DuplicateMessage_MassTransitRetry_ShouldSkip()
    {
        // Simulate MassTransit retry: cache đã có key cho MessageId → consumer bỏ qua, không ghi DB.
        var (harness, written, uow) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(
            cache: ConsumerTestHarness.AlreadySeenCache());
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-003", Guid.NewGuid(), "P2High");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task TicketCreated_FirstMessage_ClaimsShortLease_ThenExtendsTo30MinAfterWriting()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        cache.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(
            cache: cache.Object);
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-004", Guid.NewGuid(), "P2High");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        // Notification ghi ra bình thường
        written.Should().HaveCount(2);

        // Sprint 6.3 NOTI3-09 (#709) — vẫn chiếm key bằng 1 lệnh atomic SET NX EX (không còn cặp
        // GetAsync/SetAsync).
        //
        // GH-765 — nhưng lần chiếm đầu chỉ là CHỖ GIỮ ngắn. Chiếm thẳng 30 phút như bản cũ nghĩa là
        // một lỗi DB/resolver ở lần đầu sẽ khoá message suốt 30 phút: mọi lần MassTransit gửi lại
        // đều bị coi là trùng và notification biến mất hẳn.
        cache.Verify(x => x.TrySetIfNotExistsAsync(
            It.Is<string>(k => k.StartsWith("notif_msg:")),
            It.IsAny<string>(),
            // Không ghim đúng một con số: giá trị chỗ giữ có thể được chỉnh theo độ chậm thực tế
            // của resolver/DB. Điều PHẢI đúng là nó ngắn hơn hẳn cửa sổ chống trùng — bằng hoặc
            // dài hơn thì lỗi lần đầu lại khoá message suốt cả cửa sổ như bản cũ.
            It.Is<TimeSpan>(t => t > TimeSpan.Zero && t < TimeSpan.FromMinutes(30)),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cửa sổ chống trùng 30 phút chỉ được đặt SAU KHI đã ghi notification xong.
        cache.Verify(x => x.TryRefreshLeaseAsync(
            It.Is<string>(k => k.StartsWith("notif_msg:")),
            It.IsAny<string>(),
            It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(30)),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
