using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketLifecycleConsumersTests
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
