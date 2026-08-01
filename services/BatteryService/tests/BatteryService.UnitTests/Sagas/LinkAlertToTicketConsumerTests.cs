using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Consumers;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Saga.AlertTicket;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #238 — idempotency + conflict tests cho LinkAlertToTicketConsumer.
/// </summary>
public class LinkAlertToTicketConsumerTests
{
    private static Mock<IBatteryUnitOfWork> BuildUow(List<Alert> alerts)
    {
        var uow = new Mock<IBatteryUnitOfWork>();
        var repo = new Mock<IGenericRepository<Alert>>();
        repo.Setup(r => r.GetAllAsync()).Returns(alerts.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Alerts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    private static async Task<ITestHarness> StartHarness(IBatteryUnitOfWork uow)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<LinkAlertToTicketConsumer>();

                // Sửa flaky 2026-07-31 — mặc định inactivity timeout của MassTransit v8 chỉ **1 giây**.
                // `harness.Consumed.Any<T>()` trả `false` cả khi hết giờ lẫn khi hỏng thật, không phân
                // biệt được. Chạy cả solution song song thì 5 test này đỏ; chạy riêng assembly thì pass
                // 5/5. Nới trần theo khuôn `NotificationService/Helpers/ConsumerTestHarness.cs`.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(uow)
            .AddSingleton<SharedContracts.Interfaces.IIntegrationEventOutboxWriter>(Helpers.NoOpOutbox.Instance)
            .AddSingleton(NullLogger<LinkAlertToTicketConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    [Fact]
    public async Task AlertNotFound_ShouldRejectWithReason()
    {
        var uow = BuildUow(new List<Alert>());
        var harness = await StartHarness(uow.Object);

        var cmd = new LinkAlertToTicketCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T1");
        await harness.Bus.Publish(cmd);
        (await harness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().ToList();
        rejected.Should().ContainSingle();
        rejected[0].Context.Message.ErrorCode.Should().Be("ALERT_NOT_FOUND");

        await harness.Stop();
    }

    [Fact]
    public async Task AlreadyLinkedToSameTicket_ShouldIdempotentAck()
    {
        var alertId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var alert = new Alert
        {
            Id = alertId,
            BatteryAssetId = Guid.NewGuid(),
            TicketId = ticketId,
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 60,
            ActualValue = 75,
            Unit = "C",
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(new List<Alert> { alert });
        var harness = await StartHarness(uow.Object);

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, ticketId, "T1"));
        (await harness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var linked = harness.Published.Select<AlertLinkedToTicketResponse>().ToList();
        linked.Should().ContainSingle();
        linked[0].Context.Message.TicketId.Should().Be(ticketId);

        // KHÔNG được update / save lại nữa.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task AlreadyLinkedToDifferentTicket_ShouldRejectWithConflict()
    {
        var alertId = Guid.NewGuid();
        var existingTicketId = Guid.NewGuid();
        var newTicketId = Guid.NewGuid();
        var alert = new Alert
        {
            Id = alertId,
            BatteryAssetId = Guid.NewGuid(),
            TicketId = existingTicketId,
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 60,
            ActualValue = 75,
            Unit = "C",
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(new List<Alert> { alert });
        var harness = await StartHarness(uow.Object);

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, newTicketId, "T-NEW"));
        (await harness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().ToList();
        rejected.Should().ContainSingle();
        rejected[0].Context.Message.ErrorCode.Should().Be("ALERT_ALREADY_LINKED_TO_DIFFERENT_TICKET");

        await harness.Stop();
    }

    [Fact]
    public async Task HappyPath_ShouldUpdateAlertAndPublishLinkedResponse()
    {
        var alertId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var alert = new Alert
        {
            Id = alertId,
            BatteryAssetId = Guid.NewGuid(),
            TicketId = null,
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 60,
            ActualValue = 75,
            Unit = "C",
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(new List<Alert> { alert });
        var harness = await StartHarness(uow.Object);

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, ticketId, "T1"));
        (await harness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var linked = harness.Published.Select<AlertLinkedToTicketResponse>().ToList();
        linked.Should().ContainSingle();
        linked[0].Context.Message.TicketId.Should().Be(ticketId);

        alert.TicketId.Should().Be(ticketId);
        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }

    [Fact]
    public async Task SoftDeletedAlert_ShouldRejectAsNotFound()
    {
        var alertId = Guid.NewGuid();
        var alert = new Alert
        {
            Id = alertId,
            BatteryAssetId = Guid.NewGuid(),
            TicketId = null,
            IsDeleted = true,
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 60,
            ActualValue = 75,
            Unit = "C",
            DetectedAt = DateTime.UtcNow,
            DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(new List<Alert> { alert });
        var harness = await StartHarness(uow.Object);

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, Guid.NewGuid(), "T1"));
        (await harness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().ToList();
        rejected.Should().ContainSingle();
        rejected[0].Context.Message.ErrorCode.Should().Be("ALERT_NOT_FOUND");

        await harness.Stop();
    }
}
