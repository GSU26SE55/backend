using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public sealed class IotDeviceLifecycleNotificationConsumerTests
{
    [Fact]
    public async Task Recovered_WritesInAppPush_ForOperationsAndCustomer()
    {
        var operationsUser = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceRecoveredConsumer>([operationsUser]);
        var evt = new IotDeviceRecoveredEvent(
            Guid.NewGuid(),
            "GW-01",
            "Gateway 01",
            Guid.NewGuid(),
            "Site A",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(-8),
            Guid.NewGuid(),
            customer);

        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<IotDeviceRecoveredEvent>()).Should().BeTrue();
        written.Should().HaveCount(4);
        written.Select(n => n.UserId).Distinct().Should().BeEquivalentTo(new[] { operationsUser, customer });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.IotDeviceRecovered);
            n.EntityId.Should().Be(evt.IotDeviceId);
        });
        await harness.Stop();
    }

    [Fact]
    public async Task AutoDecommissioned_WritesInAppPush_ForOperations()
    {
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceAutoDecommissionedConsumer>();
        var evt = new IotDeviceAutoDecommissionedEvent(
            Guid.NewGuid(),
            "GW-BAD",
            "Gateway bad data",
            Guid.NewGuid(),
            Guid.NewGuid(),
            51,
            DateTime.UtcNow.AddMinutes(-30),
            DateTime.UtcNow);

        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<IotDeviceAutoDecommissionedEvent>()).Should().BeTrue();
        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.IotDeviceAutoDecommissioned);
            n.EntityId.Should().Be(evt.IotDeviceId);
        });
        await harness.Stop();
    }
}
