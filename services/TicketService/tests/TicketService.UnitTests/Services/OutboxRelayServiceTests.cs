using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Events;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Services;

public class OutboxRelayServiceTests
{
    private readonly Mock<IMessageProducerService> _producer = new();
    private readonly Mock<ILogger<OutboxRelayService>> _logger = new();
    private readonly IOptions<OutboxOptions> _options = Options.Create(new OutboxOptions { MaxRetryCount = 3 });

    [Fact]
    public async Task RelayBatchAsync_ValidMessage_PublishesAndMarksProcessed()
    {
        // Arrange
        var evt = new TicketCreatedIntegrationEvent(Guid.NewGuid(), "TKT-001");
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(TicketCreatedIntegrationEvent),
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow
        };
        var (uow, _, outbox, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = new OutboxRelayService(uow.Object, _producer.Object, _options, _logger.Object);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(1);
        msg.ProcessedAtUtc.Should().NotBeNull();
        _producer.Verify(x => x.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RelayBatchAsync_NoPendingMessages_ReturnsEmptyResult()
    {
        // Arrange
        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: Enumerable.Empty<OutboxMessage>());
        var sut = new OutboxRelayService(uow.Object, _producer.Object, _options, _logger.Object);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(0);
        result.Failed.Should().Be(0);
        _producer.Verify(x => x.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Since EventTypeMap is empty for now, all messages will fail as "Unknown event type"
    [Fact]
    public async Task RelayBatchAsync_UnknownEventType_IncrementsRetryCount()
    {
        // Arrange
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "UnknownEvent",
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow
        };
        var (uow, _, outbox, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = new OutboxRelayService(uow.Object, _producer.Object, _options, _logger.Object);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        msg.RetryCount.Should().Be(1);
        msg.LastError.Should().Contain("Unknown event type");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RelayBatchAsync_MaxRetryReached_IsExcludedFromQuery()
    {
        // Arrange
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "SomeEvent",
            RetryCount = 3, // MaxRetryCount is 3 in _options
            ProcessedAtUtc = null
        };
        var (uow, _, outbox, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = new OutboxRelayService(uow.Object, _producer.Object, _options, _logger.Object);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(0);
        result.Failed.Should().Be(0); // Should not even be picked up
    }
}
