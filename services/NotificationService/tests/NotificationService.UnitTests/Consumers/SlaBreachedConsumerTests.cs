using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-06 (#677) — SLA breach phân nhánh kênh theo priority (spec §3.4):
/// P1 = InApp+Push+Email+SMS · P2 = InApp+Push+Email · P3 = chỉ InApp.
/// </summary>
public class SlaBreachedConsumerTests
{
    private static SlaBreachedEvent Breach(string priority) => new()
    {
        TicketId = Guid.NewGuid(),
        BreachedAt = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc),
        Priority = priority
    };

    [Fact]
    public async Task SlaBreached_P1_Writes_AllFourChannels()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaBreachedConsumer>();
        var evt = Breach("P1Critical");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<SlaBreachedEvent>()).Should().BeTrue();

        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email,
            NotificationChannelEnum.Sms
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.SlaBreached);
            n.EntityId.Should().Be(evt.TicketId);
            n.Body.Should().Contain("P1Critical");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task SlaBreached_P2_Writes_ThreeChannels_NoSms()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaBreachedConsumer>();

        await harness.Bus.Publish(Breach("P2High"));
        (await harness.Consumed.Any<SlaBreachedEvent>()).Should().BeTrue();

        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email
        });
        written.Should().NotContain(n => n.Channel == NotificationChannelEnum.Sms);

        await harness.Stop();
    }

    [Fact]
    public async Task SlaBreached_P3_Writes_InAppOnly()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaBreachedConsumer>();

        await harness.Bus.Publish(Breach("P3Normal"));
        (await harness.Consumed.Any<SlaBreachedEvent>()).Should().BeTrue();

        written.Should().HaveCount(1);
        written[0].Channel.Should().Be(NotificationChannelEnum.InApp);

        await harness.Stop();
    }

    /// <summary>Priority lạ/rỗng → hạ về P3 để không lỡ bắn SMS vì dữ liệu không đọc được.</summary>
    [Fact]
    public async Task SlaBreached_UnknownPriority_FallsBackToInAppOnly()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<SlaBreachedConsumer>();

        await harness.Bus.Publish(Breach(string.Empty));
        (await harness.Consumed.Any<SlaBreachedEvent>()).Should().BeTrue();

        written.Should().HaveCount(1);
        written[0].Channel.Should().Be(NotificationChannelEnum.InApp);

        await harness.Stop();
    }
}
