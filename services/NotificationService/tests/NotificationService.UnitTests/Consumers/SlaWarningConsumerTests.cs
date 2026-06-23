using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class SlaWarningConsumerTests
{
    [Fact]
    public async Task SlaWarning_Writes_InAppPush()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaWarningConsumer>();
        var evt = new SlaWarningEvent
        {
            TicketId = Guid.NewGuid(),
            WarningAt = new DateTime(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc),
            Percentage = 80
        };

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<SlaWarningEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.SlaWarning);
            n.UserId.Should().Be(ConsumerTestHarness.DefaultRecipient);
            n.EntityType.Should().Be("Ticket");
            n.EntityId.Should().Be(evt.TicketId);
            n.Body.Should().Contain("80");
        });

        await harness.Stop();
    }
}
