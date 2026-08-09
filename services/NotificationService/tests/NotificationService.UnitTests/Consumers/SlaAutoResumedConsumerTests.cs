using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

public class SlaAutoResumedConsumerTests
{
    [Fact]
    public async Task WaitingCustomer_AutoResume_Writes_InAppPush_ToCustomer()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaAutoResumedConsumer>();
        var evt = new SlaAutoResumedEvent
        {
            SlaPauseEventId = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            CustomerId = ConsumerTestHarness.DefaultRecipient,
            Code = "TKT-1169",
            PauseReason = 1,
            ResumedAt = DateTime.UtcNow
        };

        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<SlaAutoResumedEvent>()).Should().BeTrue();
        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.UserId.Should().Be(evt.CustomerId);
            n.Type.Should().Be(NotificationTypeEnum.SlaAutoResumed);
            n.EntityId.Should().Be(evt.TicketId);
        });
        await harness.Stop();
    }

    [Fact]
    public async Task WaitingOnsiteSchedule_AutoResume_Writes_NoNotification()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaAutoResumedConsumer>();

        await harness.Bus.Publish(new SlaAutoResumedEvent
        {
            SlaPauseEventId = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            CustomerId = ConsumerTestHarness.DefaultRecipient,
            PauseReason = 3,
            ResumedAt = DateTime.UtcNow
        });

        (await harness.Consumed.Any<SlaAutoResumedEvent>()).Should().BeTrue();
        written.Should().BeEmpty();
        await harness.Stop();
    }

    [Fact]
    public async Task WaitingParts_AutoResume_Writes_InAppPush_ToEachActiveManager()
    {
        var managers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaAutoResumedConsumer>(managers);

        await harness.Bus.Publish(new SlaAutoResumedEvent
        {
            SlaPauseEventId = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Code = "TKT-1169",
            PauseReason = 2,
            ResumedAt = DateTime.UtcNow
        });

        (await harness.Consumed.Any<SlaAutoResumedEvent>()).Should().BeTrue();
        written.Should().HaveCount(4);
        written.Select(notification => notification.UserId).Distinct().Should().BeEquivalentTo(managers);
        written.Should().AllSatisfy(notification => notification.Type.Should().Be(NotificationTypeEnum.SlaAutoResumed));
        await harness.Stop();
    }

    [Fact]
    public async Task DuplicateDelivery_WithSamePauseEventId_WritesNotificationsOnlyOnce()
    {
        var cache = new Mock<ICacheService>();
        cache.SetupSequence(service => service.TrySetIfNotExistsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaAutoResumedConsumer>(cache: cache.Object);
        var evt = new SlaAutoResumedEvent
        {
            SlaPauseEventId = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            CustomerId = ConsumerTestHarness.DefaultRecipient,
            PauseReason = 1,
            ResumedAt = DateTime.UtcNow
        };

        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);

        (await harness.Consumed.SelectAsync<SlaAutoResumedEvent>().Count()).Should().Be(2);
        written.Should().HaveCount(2);
        await harness.Stop();
    }
}
