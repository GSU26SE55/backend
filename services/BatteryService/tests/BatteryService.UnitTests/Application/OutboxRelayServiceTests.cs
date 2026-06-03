using System.Text.Json;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

public class OutboxRelayServiceTests
{
    private static OutboxMessage MakeMessage(string type = "BatteryAnomalyDetectedEvent", DateTime? processed = null)
    {
        var evt = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "BAT-X",
            AnomalyType: 1,
            Severity: 3,
            ThresholdValue: 50,
            ActualValue: 60,
            Unit: "°C",
            DetectedAt: DateTime.UtcNow);
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            Type = type,
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow.AddSeconds(-1),
            ProcessedAtUtc = processed
        };
    }

    [Fact]
    public async Task Relay_NoPending_ReturnsZero()
    {
        var b = new MockUnitOfWorkBuilder();
        var producer = new Mock<IMessageProducerService>();
        var svc = new OutboxRelayService(b.Build(), producer.Object);

        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Published.Should().Be(0);
        producer.Verify(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relay_PendingMessage_PublishesAndMarksProcessed()
    {
        var msg = MakeMessage();
        var b = new MockUnitOfWorkBuilder().WithOutboxMessages(msg);
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var svc = new OutboxRelayService(b.Build(), producer.Object);

        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Published.Should().Be(1);
        producer.Verify(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        msg.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Relay_AlreadyProcessed_Skipped()
    {
        var processed = MakeMessage(processed: DateTime.UtcNow.AddMinutes(-5));
        var b = new MockUnitOfWorkBuilder().WithOutboxMessages(processed);
        var producer = new Mock<IMessageProducerService>();
        var svc = new OutboxRelayService(b.Build(), producer.Object);

        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Published.Should().Be(0);
        producer.Verify(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relay_UnknownType_IncrementRetryAndFail()
    {
        var msg = MakeMessage(type: "UnknownEventType");
        var b = new MockUnitOfWorkBuilder().WithOutboxMessages(msg);
        var producer = new Mock<IMessageProducerService>();
        var svc = new OutboxRelayService(b.Build(), producer.Object);

        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Failed.Should().Be(1);
        msg.RetryCount.Should().Be(1);
        msg.LastError.Should().Contain("Unknown event type");
        msg.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Relay_PublishThrows_IncrementsRetryAndFail()
    {
        var msg = MakeMessage();
        var b = new MockUnitOfWorkBuilder().WithOutboxMessages(msg);
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("RabbitMQ down"));
        var svc = new OutboxRelayService(b.Build(), producer.Object);

        var result = await svc.RelayBatchAsync(cancellationToken: CancellationToken.None);

        result.Failed.Should().Be(1);
        msg.RetryCount.Should().Be(1);
        msg.LastError.Should().Contain("RabbitMQ down");
        msg.ProcessedAtUtc.Should().BeNull();
    }
}
