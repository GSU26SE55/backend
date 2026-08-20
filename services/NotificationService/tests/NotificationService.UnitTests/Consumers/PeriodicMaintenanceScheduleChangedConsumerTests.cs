using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class PeriodicMaintenanceScheduleChangedConsumerTests
{
    [Fact]
    public async Task CustomerChange_NotifiesActiveManagers()
    {
        var managers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceScheduleChangedConsumer>(managers);
        try
        {
            await harness.Bus.Publish(Message("Customer"));

            (await harness.Consumed.Any<PeriodicMaintenanceScheduleChangedEvent>()).Should().BeTrue();
            written.Should().HaveCount(4);
            written.Select(notification => notification.UserId)
                .Distinct()
                .Should().BeEquivalentTo(managers);
            written.Should().OnlyContain(notification =>
                notification.Type == NotificationTypeEnum.PeriodicMaintenanceScheduleChanged);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task ManagerReplacement_NotifiesCustomerAndActiveManagers()
    {
        var managers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var customerId = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceScheduleChangedConsumer>(managers);
        try
        {
            await harness.Bus.Publish(Message("Manager", customerId));

            (await harness.Consumed.Any<PeriodicMaintenanceScheduleChangedEvent>()).Should().BeTrue();
            written.Should().HaveCount(6);
            written.Select(notification => notification.UserId)
                .Distinct()
                .Should().BeEquivalentTo(managers.Append(customerId));
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task DuplicateDelivery_WritesScheduleChangedNotificationsOnlyOnce()
    {
        var managers = new[] { Guid.NewGuid() };
        var message = Message("Customer", Guid.NewGuid());
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<PeriodicMaintenanceScheduleChangedConsumer>(managers);
        try
        {
            await harness.Bus.Publish(message);
            await harness.Bus.Publish(message);

            (await harness.Consumed.SelectAsync<PeriodicMaintenanceScheduleChangedEvent>().Take(2).Count())
                .Should().Be(2);
            written.Should().HaveCount(2);
            written.Select(notification => notification.Id).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static PeriodicMaintenanceScheduleChangedEvent Message(
        string role,
        Guid? customerId = null) => new(
            Guid.NewGuid(),
            "TKT-PERIODIC",
            Guid.NewGuid(),
            customerId ?? Guid.NewGuid(),
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddDays(1),
            2,
            role,
            Guid.NewGuid(),
            role == "Manager" ? "Customer confirmed by phone." : null,
            DateTime.UtcNow.AddDays(-1),
            true);
}
