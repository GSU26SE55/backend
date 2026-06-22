using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class IncidentDeclaredConsumerTests
{
    [Fact]
    public async Task IncidentDeclared_Writes_InAppPush()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<IncidentDeclaredConsumer>();
        var evt = new IncidentDeclaredEvent(Guid.NewGuid(), "TKT-011", Guid.NewGuid());

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<IncidentDeclaredEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.IncidentDeclared);
            n.UserId.Should().Be(Guid.Empty);
            n.EntityId.Should().Be(evt.TicketId);
            n.Title.Should().Contain("TKT-011");
        });

        await harness.Stop();
    }
}
