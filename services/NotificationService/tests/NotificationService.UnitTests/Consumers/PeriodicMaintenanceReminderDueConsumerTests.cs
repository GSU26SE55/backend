using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class PeriodicMaintenanceReminderDueConsumerTests
{
    [Fact]
    public async Task CustomerStage_NotifiesCustomerOncePerChannel()
    {
        var customerId = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceReminderDueConsumer>();
        try
        {
            await harness.Bus.Publish(Message(
                customerId,
                PeriodicMaintenanceReminderStage.CustomerFirstReminder));

            (await harness.Consumed.Any<PeriodicMaintenanceReminderDueEvent>()).Should().BeTrue();
            written.Should().HaveCount(2);
            written.Should().OnlyContain(notification =>
                notification.UserId == customerId &&
                notification.Type == NotificationTypeEnum.PeriodicMaintenanceReminder);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task ManagerStage_NotifiesOnlyActiveManagers()
    {
        var managers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var customerId = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceReminderDueConsumer>(managers);
        try
        {
            await harness.Bus.Publish(Message(
                customerId,
                PeriodicMaintenanceReminderStage.ManagerEscalation));

            (await harness.Consumed.Any<PeriodicMaintenanceReminderDueEvent>()).Should().BeTrue();
            written.Should().HaveCount(4);
            written.Select(notification => notification.UserId)
                .Distinct()
                .Should().BeEquivalentTo(managers);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task DuplicateDelivery_WritesReminderNotificationsOnlyOnce()
    {
        var customerId = Guid.NewGuid();
        var message = Message(customerId, PeriodicMaintenanceReminderStage.CustomerFirstReminder);
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceReminderDueConsumer>(
                cache: ConsumerTestHarness.ClaimOnceCache());
        try
        {
            await harness.Bus.Publish(message);
            await harness.Bus.Publish(message);

            (await harness.Consumed.SelectAsync<PeriodicMaintenanceReminderDueEvent>().Take(2).Count())
                .Should().Be(2);
            written.Should().HaveCount(2);
            written.Select(notification => notification.Id).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static PeriodicMaintenanceReminderDueEvent Message(
        Guid customerId,
        PeriodicMaintenanceReminderStage stage) => new(
            Guid.NewGuid(),
            "TKT-PERIODIC",
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(7),
            stage,
            false);
}
