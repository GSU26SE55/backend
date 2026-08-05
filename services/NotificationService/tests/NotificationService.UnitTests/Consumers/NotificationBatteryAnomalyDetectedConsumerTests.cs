using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class BatteryAnomalyDetectedConsumerTests
{
    [Fact]
    public async Task BatteryAnomaly_Critical_Writes_To_CustomerId_AllFourChannels()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryAnomalyDetectedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: customerId,
            AssetSerialNumber: "SN-12345",
            AnomalyType: 1,
            Severity: 3,
            ThresholdValue: 60m,
            ActualValue: 72m,
            Unit: "°C",
            DetectedAt: new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc));

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>()).Should().BeTrue();

        // Sprint 6.2 NOTI-08 (#679) — spec §3.4 T#13: Customer nhận InApp + Push + Email + SMS.
        // Preference/quiet hours lọc lại ở tầng dispatcher, không phải ở consumer.
        written.Should().HaveCount(4);
        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email,
            NotificationChannelEnum.Sms
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.BatteryAnomalyDetected);
            n.UserId.Should().Be(customerId);
            n.EntityType.Should().Be("Battery");
            n.EntityId.Should().Be(evt.BatteryAssetId);
            n.Title.Should().Contain("SN-12345");
            n.PayloadJson.Should().Contain("BatteryDetail");
        });

        await harness.Stop();
    }
}
