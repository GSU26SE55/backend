using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketResolvedConsumerTests
{
    /// <summary>
    /// Sprint 6.2 NOTI-05 (#676) — Manager (broadcast) và Customer đều được báo, mỗi bên 3 kênh.
    /// </summary>
    [Fact]
    public async Task TicketResolved_Writes_Manager_And_Customer_WithSummary()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketResolvedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketResolvedEvent(Guid.NewGuid(), "TKT-003", Guid.NewGuid(), "Battery cell replaced", customerId);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketResolvedEvent>()).Should().BeTrue();

        written.Should().HaveCount(6);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketResolved);
            n.Body.Should().Contain("Battery cell replaced");
        });

        written.Where(n => n.UserId == ConsumerTestHarness.DefaultRecipient).Should().HaveCount(3);
        written.Where(n => n.UserId == customerId).Should().HaveCount(3);

        await harness.Stop();
    }
}
