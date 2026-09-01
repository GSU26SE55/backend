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

    // Alert CAP SITE (nhiet do / do am / gas cua tu) di chung event nay nhung `BatteryAssetId` la
    // Guid.Empty vi su co nam o tu, khong thuoc vien pin nao.
    //
    // Truoc day danh sach nhan chi co khach hang: nha kho 43 do C bao cho KHACH, con Manager/Admin
    // dang truc thi khong ai duoc bao. Man "Environmental alerts" vi the khong nhan duoc tin hieu
    // realtime nao va roi han ve poll 30 giay.
    [Fact]
    public async Task SiteLevelAnomaly_AlsoNotifiesManagerAndAdmin()
    {
        var operatorId = Guid.NewGuid();
        var rolesRequested = new List<string[]>();
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryAnomalyDetectedConsumer>(
            recipients: new[] { operatorId }, rolesRequested: rolesRequested);

        var customerId = Guid.NewGuid();
        var evt = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.Empty,   // cap site
            CustomerId: customerId,
            AssetSerialNumber: "DEMO-V2",
            AnomalyType: 9,               // HighAmbientTemp
            Severity: 3,
            ThresholdValue: 43m,
            ActualValue: 69m,
            Unit: "°C",
            DetectedAt: new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>()).Should().BeTrue();

        var notified = written.Select(n => n.UserId).Distinct().ToList();
        notified.Should().Contain(customerId, "khach van phai duoc bao nhu cu");
        notified.Should().Contain(operatorId, "nguoi truc moi la nguoi xu ly su co moi truong");
        rolesRequested.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new[] { "Manager", "Admin" });

        await harness.Stop();
    }

    // Alert cua MOT vien pin giu nguyen hanh vi cu: chi khach. Khong bien moi canh bao pin thanh
    // thong bao cho toan bo nguoi truc.
    [Fact]
    public async Task BatteryLevelAnomaly_DoesNotNotifyOperators()
    {
        var rolesRequested = new List<string[]>();
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryAnomalyDetectedConsumer>(
            rolesRequested: rolesRequested);

        var customerId = Guid.NewGuid();
        await harness.Bus.Publish(new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),   // thuoc mot vien pin cu the
            CustomerId: customerId,
            AssetSerialNumber: "SN-12345",
            AnomalyType: 1,
            Severity: 3,
            ThresholdValue: 60m,
            ActualValue: 72m,
            Unit: "°C",
            DetectedAt: new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));
        (await harness.Consumed.Any<BatteryAnomalyDetectedEvent>()).Should().BeTrue();

        written.Select(n => n.UserId).Distinct().Should().ContainSingle().Which.Should().Be(customerId);
        rolesRequested.Should().BeEmpty("khong duoc hoi resolver cho alert cap pin");

        await harness.Stop();
    }
}
