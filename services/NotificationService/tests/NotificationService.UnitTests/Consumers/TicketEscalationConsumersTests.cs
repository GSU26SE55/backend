using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketEscalationConsumersTests
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
