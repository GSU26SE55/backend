using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class AlertEscalationServiceTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static BatteryAsset MakeAsset() => new()
    {
        Id = AssetId,
        SerialNumber = "BAT-001",
        BatteryTypeId = Guid.NewGuid(),
        CustomerId = CustomerId,
        InstallDate = DateTime.UtcNow.AddDays(-30),
        Status = BatteryStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static Alert MakeAlert(DateTime detectedAt,
        AlertSeverityEnum severity = AlertSeverityEnum.Critical,
        AlertStatusEnum status = AlertStatusEnum.Open) => new()
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = AssetId,
            BatteryAsset = MakeAsset(),
            AnomalyType = AnomalyTypeEnum.Overheat,
            Severity = severity,
            Status = status,
            ThresholdValue = 50,
            ActualValue = 60,
            Unit = "°C",
            DetectedAt = detectedAt,
            DedupWindowEndUtc = detectedAt.AddMinutes(30),
            CreatedAt = detectedAt
        };

    [Fact]
    public async Task Escalate_NoStaleAlerts_ReturnsZero()
    {
        var b = new MockUnitOfWorkBuilder();
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(0);
    }

    [Fact]
    public async Task Escalate_StaleCritical_EnqueuesEscalationOutbox()
    {
        var stale = MakeAlert(DateTime.UtcNow.AddMinutes(-10));
        var b = new MockUnitOfWorkBuilder().WithAlerts(stale);
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(1);
        b.OutboxMessages.Verify(r => r.AddAsync(It.Is<OutboxMessage>(m =>
            m.Type == "BatteryAlertEscalationRequestedEvent"
            && m.AggregateId == stale.Id)), Times.Once);
    }

    [Fact]
    public async Task Escalate_RecentCritical_NotEscalated()
    {
        var recent = MakeAlert(DateTime.UtcNow.AddMinutes(-2));
        var b = new MockUnitOfWorkBuilder().WithAlerts(recent);
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(0);
        b.OutboxMessages.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>()), Times.Never);
    }

    [Fact]
    public async Task Escalate_WarningAlert_NotEscalated()
    {
        var warning = MakeAlert(DateTime.UtcNow.AddMinutes(-10), severity: AlertSeverityEnum.Warning);
        var b = new MockUnitOfWorkBuilder().WithAlerts(warning);
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(0);
    }

    [Fact]
    public async Task Escalate_AcknowledgedAlert_NotEscalated()
    {
        var ack = MakeAlert(DateTime.UtcNow.AddMinutes(-10), status: AlertStatusEnum.Acknowledged);
        var b = new MockUnitOfWorkBuilder().WithAlerts(ack);
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(0);
    }

    [Fact]
    public async Task Escalate_AlreadyEscalated_SkipsDuplicate()
    {
        var stale = MakeAlert(DateTime.UtcNow.AddMinutes(-10));
        var existingOutbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = stale.Id,
            Type = "BatteryAlertEscalationRequestedEvent",
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-3)
        };
        var b = new MockUnitOfWorkBuilder()
            .WithAlerts(stale)
            .WithOutboxMessages(existingOutbox);
        var svc = new AlertEscalationService(b.Build());

        var result = await svc.EscalateAsync(minutesUntilEscalate: 5, cancellationToken: CancellationToken.None);

        result.Escalated.Should().Be(0);
        result.AlreadyEscalated.Should().Be(1);
    }
}
