using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketAssignedConsumerTests
{
    [Fact]
    public async Task TicketAssigned_Writes_To_StaffId()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketAssignedConsumer>();
        var staffId = Guid.NewGuid();
        var evt = new TicketAssignedEvent(Guid.NewGuid(), "TKT-002", staffId, "P1");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketAssignedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketAssigned);
            n.UserId.Should().Be(staffId);
            n.EntityId.Should().Be(evt.TicketId);
            n.Body.Should().Contain("P1");
        });

        await harness.Stop();
    }
}
