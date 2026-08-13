using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketWorkStartedConsumerTests
{
    [Fact]
    public async Task TicketWorkStarted_WritesToCustomerAndPrimaryStaff()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketWorkStartedConsumer>();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var message = new TicketWorkStartedEvent(
            Guid.NewGuid(), "TKT-1176", customerId, staffId,
            DateTime.UtcNow, 3, "ScheduledDue");

        await harness.Bus.Publish(message);
        (await harness.Consumed.Any<TicketWorkStartedEvent>()).Should().BeTrue();

        written.Should().HaveCount(12);
        written.Take(6).Should().AllSatisfy(notification =>
            notification.Type.Should().Be(NotificationTypeEnum.TicketAssigned));
        written.Skip(6).Should().AllSatisfy(notification =>
            notification.Type.Should().Be(NotificationTypeEnum.TicketWorkStarted));
        written.Should().AllSatisfy(notification => notification.EntityId.Should().Be(message.TicketId));
        written.Select(notification => notification.UserId)
            .Distinct()
            .Should().BeEquivalentTo(new[] { customerId, staffId });

        await harness.Stop();
    }

    [Fact]
    public async Task TicketWorkStarted_WhenAssignmentArrivesLater_PersistsAssignmentFirstWithoutDuplicates()
    {
        var (harness, written, uow) = await ConsumerTestHarness.StartAsync<TicketWorkStartedConsumer>();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var started = new TicketWorkStartedEvent(
            ticketId, "TKT-1176", customerId, staffId,
            DateTime.UtcNow, 4, "ScheduledDue", "P1Critical", DateTime.UtcNow);

        await harness.Bus.Publish(started);
        (await harness.Consumed.Any<TicketWorkStartedEvent>()).Should().BeTrue();

        var assigned = new TicketAssignedEvent(
            ticketId, "TKT-1176", staffId, "P1Critical", customerId,
            started.ScheduledStartAtUtc, started.ScheduleVersion, WorkStartsImmediately: true);
        var assignedContext = new Mock<ConsumeContext<TicketAssignedEvent>>();
        assignedContext.SetupGet(x => x.Message).Returns(assigned);
        assignedContext.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        assignedContext.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        var assignedConsumer = new TicketAssignedConsumer(
            uow.Object, ConsumerTestHarness.ProceedCache(), NullLogger<TicketAssignedConsumer>.Instance);

        await assignedConsumer.Consume(assignedContext.Object);

        written.Should().HaveCount(12);
        written.Take(6).Should().OnlyContain(n => n.Type == NotificationTypeEnum.TicketAssigned);
        written.Skip(6).Should().OnlyContain(n => n.Type == NotificationTypeEnum.TicketWorkStarted);
        await harness.Stop();
    }

    [Fact]
    public async Task TicketWorkStarted_RedeliveryAfterCacheWindow_DeterministicIdsPreventDuplicates()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketWorkStartedConsumer>();
        var message = new TicketWorkStartedEvent(
            Guid.NewGuid(), "TKT-1176", Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow, 5, "ScheduledDue", "P2High", DateTime.UtcNow);

        await harness.Bus.Publish(message);
        await harness.Bus.Publish(message);
        (await harness.Consumed.SelectAsync<TicketWorkStartedEvent>().Take(2).Count()).Should().Be(2);

        written.Should().HaveCount(12);
        written.Select(n => n.Id).Should().OnlyHaveUniqueItems();
        await harness.Stop();
    }

    [Fact]
    public async Task TicketWorkStarted_WhenAlreadySeen_DoesNotWriteAgain()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketWorkStartedConsumer>(
            cache: ConsumerTestHarness.AlreadySeenCache());
        var message = new TicketWorkStartedEvent(
            Guid.NewGuid(), "TKT-1176", Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow, 3, "ScheduledDue");

        await harness.Bus.Publish(message);
        (await harness.Consumed.Any<TicketWorkStartedEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        await harness.Stop();
    }
}
