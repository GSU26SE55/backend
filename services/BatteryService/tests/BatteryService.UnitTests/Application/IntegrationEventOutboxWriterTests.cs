using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Moq;
using SharedContracts.Events;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B #235 — IntegrationEventOutboxWriter serializes events + writes outbox row.
/// </summary>
public class IntegrationEventOutboxWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldAddOutboxRowWithCorrectTypeName()
    {
        var builder = new MockUnitOfWorkBuilder();
        var writer = new IntegrationEventOutboxWriter(builder.Build());

        var evt = new BatteryAlertEscalationRequestedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "B-1",
            AnomalyType: 1, Severity: 3,
            ActualValue: 75m, Unit: "C",
            DetectedAt: DateTime.UtcNow,
            EscalationRequestedAt: DateTime.UtcNow,
            MinutesSinceDetection: 6);

        await writer.WriteAsync(evt);

        builder.OutboxMessages.Verify(r => r.AddAsync(
            It.Is<OutboxMessage>(m =>
                m.Type == nameof(BatteryAlertEscalationRequestedEvent) &&
                m.AggregateId == evt.Id &&
                !string.IsNullOrEmpty(m.Payload))),
            Times.Once);
    }

    [Fact]
    public async Task WriteAsync_PayloadShouldContainEventFields()
    {
        var builder = new MockUnitOfWorkBuilder();
        var writer = new IntegrationEventOutboxWriter(builder.Build());
        OutboxMessage? captured = null;
        builder.OutboxMessages.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(m => captured = m)
            .Returns(Task.CompletedTask);

        var evt = new EnvironmentalIncidentDetectedEvent(
            IncidentId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            SiteName: "Site Test",
            IncidentType: 1, Severity: 3,
            DetectedAt: DateTime.UtcNow,
            AlertId: Guid.NewGuid(),
            Description: null);

        await writer.WriteAsync(evt);

        captured.Should().NotBeNull();
        captured!.Payload.Should().Contain(evt.IncidentId.ToString());
        captured.Payload.Should().Contain(evt.SiteId.ToString());
    }

    [Fact]
    public async Task WriteAsync_TypeNameShouldUseGenericTypeNotBaseEvent()
    {
        var builder = new MockUnitOfWorkBuilder();
        var writer = new IntegrationEventOutboxWriter(builder.Build());
        OutboxMessage? captured = null;
        builder.OutboxMessages.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(m => captured = m)
            .Returns(Task.CompletedTask);

        var evt = new BatteryAnomalyDetectedV2Event(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            SiteId: null,
            AssetSerialNumber: "B-1",
            AnomalyType: 12, Severity: 3,
            ThresholdValue: 40m, ActualValue: 55m,
            Unit: "mΩ", DetectedAt: DateTime.UtcNow,
            InternalResistanceMilliohm: 55m,
            CellVoltageDeltaMv: null,
            EnvironmentalIncidentId: null);

        await writer.WriteAsync(evt);

        captured!.Type.Should().Be(nameof(BatteryAnomalyDetectedV2Event));
    }
}
