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
[CollectionDefinition("TicketClosedConsumerHarness", DisableParallelization = true)]
public sealed class TicketClosedConsumerTestCollection;

[Collection("TicketClosedConsumerHarness")]
public class TicketClosedConsumerTests
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
        Status = status
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
                x.AddConsumer<TicketClosedConsumer>();
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(uow)
            .AddSingleton(NullLogger<TicketClosedConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return new HarnessScope(provider, harness);
    }

    [Fact]
    public async Task TicketClosed_ShouldResolveLinkedOpenAlerts()
    {
        var ticketId = Guid.NewGuid();
        var openAlert = MakeAlert(ticketId, AlertStatusEnum.Open);
        var mergedAlert = MakeAlert(ticketId, AlertStatusEnum.Merged);
        var uow = BuildUow(new List<Alert> { openAlert, mergedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<TicketClosedConsumer>();

        await harness.Bus.Publish(new TicketClosedEvent(
            ticketId, "T1", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: null));
        (await consumerHarness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        openAlert.Status.Should().Be(AlertStatusEnum.Resolved);
        mergedAlert.Status.Should().Be(AlertStatusEnum.Resolved);
        openAlert.ResolvedAt.Should().NotBeNull();
        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// AlertAutoResolveService (sensor-based) CỐ Ý loại trừ SohDegradation, SensorMismatch,
    /// DeviceOffline — không có tín hiệu sensor đáng tin để tự resolve (GH-783 với SOH cụ thể).
    /// TicketClosedConsumer KHÔNG suy luận từ sensor, chỉ tin quyết định nghiệp vụ của Manager,
    /// nên PHẢI resolve được cả 3 loại này khi ticket đóng — khác AlertAutoResolveService.
    /// </summary>
    [Theory]
    [InlineData(AnomalyTypeEnum.SohDegradation)]
    [InlineData(AnomalyTypeEnum.SensorMismatch)]
    [InlineData(AnomalyTypeEnum.DeviceOffline)]
    public async Task TicketClosed_ExcludedFromAutoResolve_ShouldStillResolveViaTicketClose(AnomalyTypeEnum anomalyType)
    {
        var ticketId = Guid.NewGuid();
        var alert = MakeAlert(ticketId, AlertStatusEnum.Open, anomalyType);
        var uow = BuildUow(new List<Alert> { alert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<TicketClosedConsumer>();

        await harness.Bus.Publish(new TicketClosedEvent(
            ticketId, "T1", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: null));
        (await consumerHarness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        alert.Status.Should().Be(AlertStatusEnum.Resolved);
        alert.ResolvedAt.Should().NotBeNull();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketClosed_NoLinkedAlerts_ShouldNotSave()
    {
        var uow = BuildUow(new List<Alert>());
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<TicketClosedConsumer>();

        await harness.Bus.Publish(new TicketClosedEvent(
            Guid.NewGuid(), "T1", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: null));
        (await consumerHarness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketClosed_AlertAlreadyResolved_ShouldBeSkipped()
    {
        var ticketId = Guid.NewGuid();
        var resolvedAlert = MakeAlert(ticketId, AlertStatusEnum.Resolved);
        var uow = BuildUow(new List<Alert> { resolvedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<TicketClosedConsumer>();

        await harness.Bus.Publish(new TicketClosedEvent(
            ticketId, "T1", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: null));
        (await consumerHarness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        uow.Verify(u => u.Alerts.UpdateAsync(It.IsAny<Alert>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketClosed_SoftDeletedAlert_ShouldBeIgnored()
    {
        var ticketId = Guid.NewGuid();
        var deletedAlert = MakeAlert(ticketId, AlertStatusEnum.Open);
        deletedAlert.IsDeleted = true;
        var uow = BuildUow(new List<Alert> { deletedAlert });
        await using var scope = await StartHarness(uow.Object);
        var harness = scope.Harness;
        var consumerHarness = harness.GetConsumerHarness<TicketClosedConsumer>();

        await harness.Bus.Publish(new TicketClosedEvent(
            ticketId, "T1", Guid.NewGuid(), DateTime.UtcNow, IsAutoClosed: false, Rating: null));
        (await consumerHarness.Consumed.Any<TicketClosedEvent>()).Should().BeTrue();

        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
