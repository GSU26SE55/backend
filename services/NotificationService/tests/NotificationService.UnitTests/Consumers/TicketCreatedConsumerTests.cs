using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketCreatedConsumerTests
{
    [Fact]
    public async Task TicketCreated_Writes_InAppPush_PlaceholderRecipient()
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
            n.UserId.Should().Be(Guid.Empty);
            n.EntityType.Should().Be("Ticket");
            n.EntityId.Should().Be(evt.TicketId);
            n.Title.Should().Contain("TKT-001");
            n.PayloadJson.Should().Contain("TicketDetail");
        });
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
