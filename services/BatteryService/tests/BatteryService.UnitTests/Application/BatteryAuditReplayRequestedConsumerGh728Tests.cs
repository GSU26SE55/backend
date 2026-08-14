using System.Text.Json;
using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Consumers;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-728 — kiểm khung replay audit (<c>AuditReplayRequestedConsumerBase</c>) qua bản hiện
/// thực của BatteryService. Logic nằm hết ở lớp cơ sở nên 5 service còn lại dùng chung
/// đường đi này.
/// </summary>
public class AuditReplayRequestedConsumerGh728Tests
{
    private static AuditCreatedEventV1 SampleAudit(Guid eventId, DateTime occurredAt) => new(
        EventId: eventId,
        ServiceName: AuditServiceNames.Battery,
        ActionCode: "BatteryAssetCreated",
        ActionCategory: "DataChange",
        Severity: "Info",
        TargetType: "BatteryAsset",
        TargetId: Guid.NewGuid(),
        TargetDisplay: "SN-1",
        ActorAccountId: Guid.NewGuid(),
        ActorRole: "Admin",
        ActorDisplay: "admin",
        ActorIp: null,
        ActorUserAgent: null,
        IsSuccess: true,
        ErrorCode: null,
        Reason: null,
        MetadataJson: null,
        CorrelationId: null,
        CausationId: null,
        OccurredAt: occurredAt,
        RecordedAt: occurredAt);

    private static BatteryAuditOutbox Row(Guid eventId, DateTime createdAt, string? payloadOverride = null)
        => new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = nameof(AuditCreatedEventV1),
            Payload = payloadOverride ?? JsonSerializer.Serialize(SampleAudit(eventId, createdAt)),
            CreatedAt = createdAt
        };

    /// <summary>ConsumeContext giả — ghi lại mọi thứ được publish.</summary>
    private static (Mock<ConsumeContext<AuditReplayRequestedEvent>> Ctx, List<object> Published)
        BuildContext(AuditReplayRequestedEvent evt)
    {
        var published = new List<object>();
        var ctx = new Mock<ConsumeContext<AuditReplayRequestedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        ctx.Setup(c => c.Publish(It.IsAny<AuditCreatedEventV1>(), It.IsAny<CancellationToken>()))
            .Callback<AuditCreatedEventV1, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);
        ctx.Setup(c => c.Publish(It.IsAny<AuditReplayCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AuditReplayCompletedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        return (ctx, published);
    }

    private static BatteryAuditReplayRequestedConsumer Sut(MockUnitOfWorkBuilder b) =>
        new(b.Build(), NullLogger<BatteryAuditReplayRequestedConsumer>.Instance);

    [Fact]
    public async Task Consume_RepublishesRowsAndReportsCompletion()
    {
        var now = DateTime.UtcNow;
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAuditOutboxes(Row(id1, now.AddMinutes(-10)), Row(id2, now.AddMinutes(-5)));

        var (ctx, published) = BuildContext(
            new AuditReplayRequestedEvent(Guid.NewGuid(), null, null, null, now));

        await Sut(b).Consume(ctx.Object);

        var audits = published.OfType<AuditCreatedEventV1>().ToList();
        audits.Should().HaveCount(2);
        // EventId PHẢI giữ nguyên — đó là thứ khiến aggregator khử trùng được.
        audits.Select(a => a.EventId).Should().BeEquivalentTo(new[] { id1, id2 });

        var done = published.OfType<AuditReplayCompletedEvent>().Should().ContainSingle().Subject;
        done.ServiceName.Should().Be(AuditServiceNames.Battery);
        done.RepublishedCount.Should().Be(2);
        done.IsSuccess.Should().BeTrue();
        done.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Consume_RequestForAnotherService_DoesNothingAtAll()
    {
        // Publish là fanout: mọi service đều nhận. Không phải việc của mình thì KHÔNG được
        // báo cáo, nếu không aggregator đếm nhầm số service đã phản hồi và đóng job sớm.
        var b = new MockUnitOfWorkBuilder().WithBatteryAuditOutboxes(Row(Guid.NewGuid(), DateTime.UtcNow));

        var (ctx, published) = BuildContext(new AuditReplayRequestedEvent(
            Guid.NewGuid(), AuditServiceNames.Auth, null, null, DateTime.UtcNow));

        await Sut(b).Consume(ctx.Object);

        published.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_RequestForOwnService_IsHandled()
    {
        var b = new MockUnitOfWorkBuilder().WithBatteryAuditOutboxes(Row(Guid.NewGuid(), DateTime.UtcNow));

        var (ctx, published) = BuildContext(new AuditReplayRequestedEvent(
            Guid.NewGuid(), "batteryservice", null, null, DateTime.UtcNow)); // khác hoa thường

        await Sut(b).Consume(ctx.Object);

        published.OfType<AuditCreatedEventV1>().Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_FiltersByTimeRange()
    {
        var now = DateTime.UtcNow;
        var inRange = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder().WithBatteryAuditOutboxes(
            Row(Guid.NewGuid(), now.AddDays(-10)),   // trước From
            Row(inRange, now.AddDays(-2)),
            Row(Guid.NewGuid(), now.AddDays(5)));    // sau To

        var (ctx, published) = BuildContext(new AuditReplayRequestedEvent(
            Guid.NewGuid(), null, now.AddDays(-3), now, now));

        await Sut(b).Consume(ctx.Object);

        published.OfType<AuditCreatedEventV1>().Should().ContainSingle()
            .Which.EventId.Should().Be(inRange);
    }

    [Fact]
    public async Task Consume_SoftDeletedRows_AreSkipped()
    {
        var deleted = Row(Guid.NewGuid(), DateTime.UtcNow);
        deleted.IsDeleted = true;
        var b = new MockUnitOfWorkBuilder().WithBatteryAuditOutboxes(deleted);

        var (ctx, published) = BuildContext(
            new AuditReplayRequestedEvent(Guid.NewGuid(), null, null, null, DateTime.UtcNow));

        await Sut(b).Consume(ctx.Object);

        published.OfType<AuditCreatedEventV1>().Should().BeEmpty();
        published.OfType<AuditReplayCompletedEvent>().Should().ContainSingle()
            .Which.RepublishedCount.Should().Be(0);
    }

    [Fact]
    public async Task Consume_CorruptPayload_SkipsRowButKeepsGoing_AndFlagsTruncated()
    {
        // Một dòng hỏng KHÔNG được làm hỏng cả lần replay; nhưng job cũng KHÔNG được báo
        // "đủ" vì thực tế đã thiếu mất một bản ghi.
        var good = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder().WithBatteryAuditOutboxes(
            Row(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10), payloadOverride: "{ hỏng"),
            Row(good, DateTime.UtcNow.AddMinutes(-5)));

        var (ctx, published) = BuildContext(
            new AuditReplayRequestedEvent(Guid.NewGuid(), null, null, null, DateTime.UtcNow));

        await Sut(b).Consume(ctx.Object);

        published.OfType<AuditCreatedEventV1>().Should().ContainSingle()
            .Which.EventId.Should().Be(good);

        var done = published.OfType<AuditReplayCompletedEvent>().Should().ContainSingle().Subject;
        done.IsSuccess.Should().BeTrue("một payload hỏng không phải lỗi hệ thống");
        done.Truncated.Should().BeTrue("dữ liệu replay KHÔNG đầy đủ");
    }

    [Fact]
    public async Task Consume_EmptyOutbox_StillReportsCompletion()
    {
        // Không báo cáo = job ở aggregator treo vĩnh viễn.
        var b = new MockUnitOfWorkBuilder();

        var (ctx, published) = BuildContext(
            new AuditReplayRequestedEvent(Guid.NewGuid(), null, null, null, DateTime.UtcNow));

        await Sut(b).Consume(ctx.Object);

        published.OfType<AuditReplayCompletedEvent>().Should().ContainSingle()
            .Which.RepublishedCount.Should().Be(0);
    }
}
