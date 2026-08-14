using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketScheduleChangedConsumerTests
{
    [Fact]
    public async Task TicketScheduleChanged_NotifiesCustomerPrimaryStaffAndManagers()
    {
        var managerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketScheduleChangedConsumer>(
            recipients: new[] { managerId });
        var message = new TicketScheduleChangedEvent(
            Guid.NewGuid(), "TKT-1176", customerId, staffId,
            DateTime.UtcNow, DateTime.UtcNow.AddHours(2), 4);

        await harness.Bus.Publish(message);
        (await harness.Consumed.Any<TicketScheduleChangedEvent>()).Should().BeTrue();

        written.Should().HaveCount(9);
        written.Select(notification => notification.UserId)
            .Distinct()
            .Should().BeEquivalentTo(new[] { customerId, staffId, managerId });
        written.Should().AllSatisfy(notification =>
            notification.Type.Should().Be(NotificationTypeEnum.TicketScheduleChanged));

        await harness.Stop();
    }
}
