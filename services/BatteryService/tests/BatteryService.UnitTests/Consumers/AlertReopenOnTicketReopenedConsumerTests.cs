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
using SharedContracts.Events;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Consumers;

/// <summary>
/// MassTransit harnesses start background bus workers — chạy tuần tự để tránh flaky do
/// tranh worker, giống <c>LinkAlertToTicketConsumerTests</c>.
/// </summary>
[CollectionDefinition("AlertReopenOnTicketReopenedConsumerHarness", DisableParallelization = true)]
public sealed class AlertReopenOnTicketReopenedConsumerTestCollection;

[Collection("AlertReopenOnTicketReopenedConsumerHarness")]
public class AlertReopenOnTicketReopenedConsumerTests
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

    private static Alert MakeAlert(Guid ticketId, AlertStatusEnum status, AnomalyTypeEnum type = AnomalyTypeEnum.HighAmbientTemp) => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = null,
        TicketId = ticketId,
        AnomalyType = type,
        Severity = AlertSeverityEnum.Critical,
        ThresholdValue = 45,
        ActualValue = 50,
        Unit = "°C",
        DetectedAt = DateTime.UtcNow,
        DedupWindowEndUtc = DateTime.UtcNow.AddHours(1),
        CreatedAt = DateTime.UtcNow,
        Status = status,
        ResolvedAt = status == AlertStatusEnum.Resolved ? DateTime.UtcNow : null
    };

    private static Mock<IBatteryUnitOfWork> BuildUow(List<Alert> alerts)
    {
        var uow = new Mock<IBatteryUnitOfWork>();
        var repo = new Mock<IGenericRepository<Alert>>();
        repo.Setup(r => r.GetAllAsync()).Returns(alerts.AsQueryable().BuildMock());
        repo.Setup(r => r.UpdateAsync(It.IsAny<Alert>()));
        uow.SetupGet(u => u.Alerts).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    private static async Task<HarnessScope> StartHarness(IBatteryUnitOfWork uow)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<AlertReopenOnTicketReopenedConsumer>();
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(uow)
            .AddSingleton(NullLogger<AlertReopenOnTicketReopenedConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return new HarnessScope(provider, harness);
    }

    [Fact]
    public async Task TicketReopened_ShouldRevertResolvedAlertsBackToOpen()
    {
        var ticketId = Guid.NewGuid();
        var resolvedAlert = MakeAlert(ticketId, AlertStatusEnum.Resolved);
        var previousClosedAt = DateTime.UtcNow.AddMinutes(-5);
        resolvedAlert.ResolvedAt = previousClosedAt;
        var uow = BuildUow(new List<Alert> { resolvedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<AlertReopenOnTicketReopenedConsumer>();

        await harness.Bus.Publish(new TicketReopenedEvent(
            ticketId, "T1", Guid.NewGuid(), null, "Still broken", 1, DateTime.UtcNow,
            previousClosedAt));
        (await consumerHarness.Consumed.Any<TicketReopenedEvent>()).Should().BeTrue();

        resolvedAlert.Status.Should().Be(AlertStatusEnum.Open);
        resolvedAlert.ResolvedAt.Should().BeNull();
        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketReopened_NoResolvedAlerts_ShouldNotSave()
    {
        var ticketId = Guid.NewGuid();
        var openAlert = MakeAlert(ticketId, AlertStatusEnum.Open);
        var uow = BuildUow(new List<Alert> { openAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<AlertReopenOnTicketReopenedConsumer>();

        await harness.Bus.Publish(new TicketReopenedEvent(
            ticketId, "T1", Guid.NewGuid(), null, "Still broken", 1, DateTime.UtcNow));
        (await consumerHarness.Consumed.Any<TicketReopenedEvent>()).Should().BeTrue();

        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketReopened_AlertResolvedBeforePreviousCloseCycle_IsNotReopened()
    {
        var ticketId = Guid.NewGuid();
        var unrelatedResolvedAlert = MakeAlert(ticketId, AlertStatusEnum.Resolved);
        unrelatedResolvedAlert.ResolvedAt = DateTime.UtcNow.AddDays(-30);
        var previousClosedAt = DateTime.UtcNow.AddMinutes(-5);
        var uow = BuildUow(new List<Alert> { unrelatedResolvedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<AlertReopenOnTicketReopenedConsumer>();

        await harness.Bus.Publish(new TicketReopenedEvent(
            ticketId, "T1", Guid.NewGuid(), null, "Still broken", 1, DateTime.UtcNow,
            previousClosedAt));
        (await consumerHarness.Consumed.Any<TicketReopenedEvent>()).Should().BeTrue();

        unrelatedResolvedAlert.Status.Should().Be(AlertStatusEnum.Resolved);
        unrelatedResolvedAlert.ResolvedAt.Should().NotBeNull();
        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketReopened_SoftDeletedAlert_ShouldBeIgnored()
    {
        var ticketId = Guid.NewGuid();
        var deletedAlert = MakeAlert(ticketId, AlertStatusEnum.Resolved);
        deletedAlert.IsDeleted = true;
        var uow = BuildUow(new List<Alert> { deletedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<AlertReopenOnTicketReopenedConsumer>();

        await harness.Bus.Publish(new TicketReopenedEvent(
            ticketId, "T1", Guid.NewGuid(), null, "Still broken", 1, DateTime.UtcNow));
        (await consumerHarness.Consumed.Any<TicketReopenedEvent>()).Should().BeTrue();

        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
