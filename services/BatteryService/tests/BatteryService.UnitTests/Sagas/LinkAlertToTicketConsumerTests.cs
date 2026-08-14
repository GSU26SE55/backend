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
/// MassTransit harnesses start background bus workers. Running these tests in parallel with the
/// rest of the large BatteryService suite can starve those workers on a small CI executor and make
/// <c>Consumed.Any&lt;T&gt;()</c> time out even though the consumer is wired correctly.
/// </summary>
[CollectionDefinition("LinkAlertToTicketConsumerHarness", DisableParallelization = true)]
public sealed class LinkAlertToTicketConsumerTestCollection;

/// <summary>
/// Sprint 5B #238 — idempotency + conflict tests cho LinkAlertToTicketConsumer.
/// </summary>
[Collection("LinkAlertToTicketConsumerHarness")]
public class LinkAlertToTicketConsumerTests
{
    private sealed class HarnessScope : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public HarnessScope(ServiceProvider provider, ITestHarness harness)
        {
            _provider = provider;
            Harness = harness;
        }

        public ITestHarness Harness { get; }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await _provider.DisposeAsync();
        }
    }

    private static Mock<IBatteryUnitOfWork> BuildUow(List<Alert> alerts)
    {
        var uow = new Mock<IBatteryUnitOfWork>();
        var repo = new Mock<IGenericRepository<Alert>>();
        repo.Setup(r => r.GetAllAsync()).Returns(alerts.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Alerts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    private static async Task<HarnessScope> StartHarness(IBatteryUnitOfWork uow)
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
        return new HarnessScope(provider, harness);
    }

    [Fact]
    public async Task AlertNotFound_ShouldRejectWithReason()
    {
        var uow = BuildUow(new List<Alert>());
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<LinkAlertToTicketConsumer>();

        var cmd = new LinkAlertToTicketCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T1");
        await harness.Bus.Publish(cmd);
        (await consumerHarness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().First();
        rejected.Context.Message.ErrorCode.Should().Be("ALERT_NOT_FOUND");
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
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<LinkAlertToTicketConsumer>();

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, ticketId, "T1"));
        (await consumerHarness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var linked = harness.Published.Select<AlertLinkedToTicketResponse>().First();
        linked.Context.Message.TicketId.Should().Be(ticketId);

        // KHÔNG được update / save lại nữa.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<LinkAlertToTicketConsumer>();

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, newTicketId, "T-NEW"));
        (await consumerHarness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().First();
        rejected.Context.Message.ErrorCode.Should().Be("ALERT_ALREADY_LINKED_TO_DIFFERENT_TICKET");
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
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<LinkAlertToTicketConsumer>();

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, ticketId, "T1"));
        (await consumerHarness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var linked = harness.Published.Select<AlertLinkedToTicketResponse>().First();
        linked.Context.Message.TicketId.Should().Be(ticketId);

        alert.TicketId.Should().Be(ticketId);
        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<LinkAlertToTicketConsumer>();

        await harness.Bus.Publish(new LinkAlertToTicketCommand(alertId, alertId, Guid.NewGuid(), "T1"));
        (await consumerHarness.Consumed.Any<LinkAlertToTicketCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<AlertLinkToTicketRejected>().First();
        rejected.Context.Message.ErrorCode.Should().Be("ALERT_NOT_FOUND");
    }
}
