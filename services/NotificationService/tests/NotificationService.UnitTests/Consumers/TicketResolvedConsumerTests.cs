using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketResolvedConsumerTests
{
    [Fact]
    public async Task TicketResolved_Writes_PlaceholderRecipient_WithSummary()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketResolvedConsumer>();
        var evt = new TicketResolvedEvent(Guid.NewGuid(), "TKT-003", Guid.NewGuid(), "Đã thay cell pin");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketResolvedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketResolved);
            n.UserId.Should().Be(Guid.Empty);
            n.Body.Should().Contain("Đã thay cell pin");
        });

        await harness.Stop();
    }
}
