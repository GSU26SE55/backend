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
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-001");

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
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-002");

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
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-003");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task TicketCreated_FirstMessage_ShouldSetDebounceKey_30Min()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(
            cache: cache.Object);
        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-004");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        // Notification ghi ra bình thường
        written.Should().HaveCount(2);

        // SetAsync phải được gọi với window 30 phút (MessageWindow)
        cache.Verify(x => x.SetAsync(
            It.Is<string>(k => k.StartsWith("notif_msg:")),
            It.IsAny<string>(),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(30)),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
