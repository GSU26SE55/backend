using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketEscalatedConsumerTests
{
    [Fact]
    public async Task TicketEscalated_Writes_InAppPush_WithReason()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketEscalatedConsumer>();
        var evt = new TicketEscalatedEvent(Guid.NewGuid(), "TKT-010", 2, "SLA breach", Guid.NewGuid(), "Staff A");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketEscalatedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketEscalated);
            n.UserId.Should().Be(Guid.Empty);
            n.EntityId.Should().Be(evt.TicketId);
            n.PayloadJson.Should().Contain("\"reason\":2");
            n.Body.Should().Contain("SLA breach");
        });

        await harness.Stop();
    }
}
