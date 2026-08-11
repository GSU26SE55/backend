using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Events.Chats;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 6.2 — test cho các consumer mới: NOTI-03 (#674), NOTI-07 (#678), NOTI-08 (#679).
/// </summary>
public class ChatEscalatedToAdminConsumerTests
{
    [Fact]
    public async Task ChatEscalated_NotifiesAdmin_OnThreeChannels()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<ChatEscalatedToAdminConsumer>();
        var evt = new ChatEscalatedToAdminEvent(Guid.NewGuid(), Guid.NewGuid(), "TKT-777", Guid.NewGuid());

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatEscalatedToAdminEvent>()).Should().BeTrue();

        written.Should().HaveCount(3);
        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.ChatEscalatedToAdmin);
            n.UserId.Should().Be(ConsumerTestHarness.DefaultRecipient);
            n.EntityId.Should().Be(evt.TicketId);
            n.Title.Should().Contain("TKT-777");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task ChatEscalated_NoAdminResolved_WritesNothing()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<ChatEscalatedToAdminConsumer>(
            recipients: Array.Empty<Guid>());

        await harness.Bus.Publish(new ChatEscalatedToAdminEvent(Guid.NewGuid(), Guid.NewGuid(), "TKT-1", Guid.NewGuid()));
        (await harness.Consumed.Any<ChatEscalatedToAdminEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        await harness.Stop();
    }
}

public class BatteryAnomalyWarningConsumerTests
{
    private const int SeverityInfo = 1;
    private const int SeverityWarning = 2;

    private static BatteryAnomalyWarningDetectedEvent Evt(Guid customerId, int severity) => new(
        AlertId: Guid.NewGuid(),
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: customerId,
        AssetSerialNumber: "SN-777",
        AnomalyType: 1,
        Severity: severity,
        ThresholdValue: 50m,
        ActualValue: 53m,
        Unit: "°C",
        DetectedAt: new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));

    /// <summary>Spec §3.4 T#12 — Warning: Customer nhận InApp + Push.</summary>
    [Fact]
    public async Task Warning_Writes_InAppAndPush_ToCustomer()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<BatteryAnomalyWarningConsumer>();
        var customerId = Guid.NewGuid();

        await harness.Bus.Publish(Evt(customerId, SeverityWarning));
        (await harness.Consumed.Any<BatteryAnomalyWarningDetectedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.BatteryAnomalyWarning);
            n.UserId.Should().Be(customerId);
            n.EntityType.Should().Be("Battery");
        });

        await harness.Stop();
    }

    /// <summary>Spec §3.4 T#11 — Info: chỉ InApp, không push để khỏi làm phiền.</summary>
    [Fact]
    public async Task Info_Writes_InAppOnly()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<BatteryAnomalyWarningConsumer>();

        await harness.Bus.Publish(Evt(Guid.NewGuid(), SeverityInfo));
        (await harness.Consumed.Any<BatteryAnomalyWarningDetectedEvent>()).Should().BeTrue();

        written.Should().HaveCount(1);
        written[0].Channel.Should().Be(NotificationChannelEnum.InApp);
        written[0].Type.Should().Be(NotificationTypeEnum.BatteryAnomalyInfo);

        await harness.Stop();
    }

    [Fact]
    public async Task EmptyCustomerId_WritesNothing()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<BatteryAnomalyWarningConsumer>();

        await harness.Bus.Publish(Evt(Guid.Empty, SeverityWarning));
        (await harness.Consumed.Any<BatteryAnomalyWarningDetectedEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        await harness.Stop();
    }
}

public class TicketLifecycleConsumerTests
{
    [Fact]
    public async Task TicketApproved_NotifiesCustomer_WithRatingCallToAction()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketApprovedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketApprovedEvent(
            Guid.NewGuid(), "TKT-100", customerId, Guid.NewGuid(), "Meets requirements", DateTime.UtcNow);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketApprovedEvent>()).Should().BeTrue();

        written.Should().HaveCount(3);
        written.Should().AllSatisfy(n =>
        {
            n.UserId.Should().Be(customerId);
            n.Type.Should().Be(NotificationTypeEnum.TicketApproved);
            n.PayloadJson.Should().Contain("TicketRate");
            n.Body.Should().Contain("rate the quality");
        });

        await harness.Stop();
    }

    /// <summary>Từ chối kết quả resolve → người cần biết là Staff, không phải Customer.</summary>
    [Fact]
    public async Task TicketRejected_ResolutionRejected_NotifiesStaff()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketRejectedConsumer>();
        var staffId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = new TicketRejectedEvent(
            Guid.NewGuid(), "TKT-101", customerId, staffId, "Cell not yet replaced", IsClosedRejected: false, DateTime.UtcNow);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketRejectedEvent>()).Should().BeTrue();

        written.Should().NotBeEmpty();
        written.Should().AllSatisfy(n => n.UserId.Should().Be(staffId));
        written.Should().AllSatisfy(n => n.UserId.Should().NotBe(customerId));

        await harness.Stop();
    }

    /// <summary>Từ chối ở triage (ngoài scope) → người cần biết là Customer.</summary>
    [Fact]
    public async Task TicketRejected_ClosedRejected_NotifiesCustomer()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketRejectedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketRejectedEvent(
            Guid.NewGuid(), "TKT-102", customerId, Guid.NewGuid(), "Out of scope", IsClosedRejected: true, DateTime.UtcNow);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketRejectedEvent>()).Should().BeTrue();

        written.Should().NotBeEmpty();
        written.Should().AllSatisfy(n => n.UserId.Should().Be(customerId));

        await harness.Stop();
    }

    [Fact]
    public async Task TicketClosed_AutoClosed_NotifiesCustomerAndManager()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketClosedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketClosedEvent(
            Guid.NewGuid(), "TKT-103", customerId, DateTime.UtcNow, IsAutoClosed: true, Rating: null);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        written.Select(n => n.UserId).Distinct().Should().BeEquivalentTo(new[]
        {
            customerId, ConsumerTestHarness.DefaultRecipient
        });
        written.Should().AllSatisfy(n => n.Body.Should().Contain("automatically closed"));

        await harness.Stop();
    }

    [Fact]
    public async Task TicketClosed_WithRating_MentionsRatingInBody()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketClosedConsumer>();
        var evt = new TicketClosedEvent(
            Guid.NewGuid(), "TKT-104", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: 5);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        written.Should().AllSatisfy(n => n.Body.Should().Contain("5-star"));
        await harness.Stop();
    }

    [Fact]
    public async Task TicketReopened_NotifiesManagerAndAssignedStaff()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketReopenedConsumer>();
        var staffId = Guid.NewGuid();
        var evt = new TicketReopenedEvent(
            Guid.NewGuid(), "TKT-105", Guid.NewGuid(), staffId, "Still broken", 2, DateTime.UtcNow);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketReopenedEvent>()).Should().BeTrue();

        written.Select(n => n.UserId).Distinct().Should().BeEquivalentTo(new[]
        {
            ConsumerTestHarness.DefaultRecipient, staffId
        });

        await harness.Stop();
    }

    [Fact]
    public async Task TicketStatusChanged_NotifiesCustomer_WithStatusNames()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketStatusChangedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketStatusChangedEvent(
            Guid.NewGuid(), "TKT-106", customerId, Guid.NewGuid(), 3, 4, "Assigned", "InProgress");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketStatusChangedEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().AllSatisfy(n =>
        {
            n.UserId.Should().Be(customerId);
            n.Type.Should().Be(NotificationTypeEnum.TicketStatusChanged);
            n.Body.Should().Contain("Assigned").And.Contain("InProgress");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task TicketRatingRequested_TellsCustomerRemainingDays()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketRatingRequestedConsumer>();
        var customerId = Guid.NewGuid();
        var evt = new TicketRatingRequestedEvent(
            Guid.NewGuid(), "TKT-107", customerId, DateTime.UtcNow.AddDays(-3), 3, 4);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketRatingRequestedEvent>()).Should().BeTrue();

        written.Should().NotBeEmpty();
        written.Should().AllSatisfy(n =>
        {
            n.UserId.Should().Be(customerId);
            n.Type.Should().Be(NotificationTypeEnum.TicketRatingRequested);
            n.Body.Should().Contain("4 day(s)");
        });

        await harness.Stop();
    }
}

/// <summary>Sprint 6.2 NOTI-11 (#682) — SMS thất bại phải kéo record notification về Failed.</summary>
public class SmsFailedConsumerTests
{
    private static Domain.Entities.Notification SmsNotification(Guid id, NotificationStatusEnum status) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        Type = NotificationTypeEnum.SlaBreached,
        Channel = NotificationChannelEnum.Sms,
        Status = status,
        Title = "T",
        Body = "B",
        SentAt = DateTime.UtcNow,
    };

    private static SmsFailedEvent Failed(Guid correlationId, string source = "notification") => new(
        SmsId: Guid.NewGuid(),
        CorrelationId: correlationId,
        PhoneNumber: "0901234567",
        SourceService: source,
        ErrorMessage: "SIM out of credit",
        FailedAt: DateTime.UtcNow,
        FinalFailure: true);

    private static SmsFailedConsumer Build(Domain.Entities.Notification? seed, out Mock<ConsumeContext<SmsFailedEvent>> _)
    {
        _ = new Mock<ConsumeContext<SmsFailedEvent>>();
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: seed is null ? [] : [seed]);
        return new SmsFailedConsumer(uow.Object, NullLogger<SmsFailedConsumer>.Instance);
    }

    private static ConsumeContext<SmsFailedEvent> Context(SmsFailedEvent evt)
    {
        var ctx = new Mock<ConsumeContext<SmsFailedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task SmsFailed_MarksMatchingNotificationFailed()
    {
        var id = Guid.NewGuid();
        var noti = SmsNotification(id, NotificationStatusEnum.Sent);
        var sut = Build(noti, out _);

        await sut.Consume(Context(Failed(id)));

        noti.Status.Should().Be(NotificationStatusEnum.Failed);
        noti.SentAt.Should().BeNull();
        noti.FailureReason.Should().Contain("SIM out of credit");
    }

    [Fact]
    public async Task SmsFailed_FromOtherService_IsIgnored()
    {
        var id = Guid.NewGuid();
        var noti = SmsNotification(id, NotificationStatusEnum.Sent);
        var sut = Build(noti, out _);

        await sut.Consume(Context(Failed(id, source: "auth")));

        noti.Status.Should().Be(NotificationStatusEnum.Sent, "SMS OTP của AuthService không có record notification");
    }

    [Fact]
    public async Task SmsFailed_AlreadyRead_DoesNotDowngrade()
    {
        var id = Guid.NewGuid();
        var noti = SmsNotification(id, NotificationStatusEnum.Read);
        var sut = Build(noti, out _);

        await sut.Consume(Context(Failed(id)));

        noti.Status.Should().Be(NotificationStatusEnum.Read);
    }

    [Fact]
    public async Task SmsFailed_UnknownCorrelation_DoesNotThrow()
    {
        var sut = Build(null, out _);

        var act = async () => await sut.Consume(Context(Failed(Guid.NewGuid())));

        await act.Should().NotThrowAsync();
    }
}
