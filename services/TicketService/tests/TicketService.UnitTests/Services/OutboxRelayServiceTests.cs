using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Events.Chats;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Services;

public class OutboxRelayServiceTests
{
    private readonly Mock<IIntegrationEventTransport> _transport = new();
    private readonly Mock<ILogger<OutboxRelayService>> _logger = new();
    private readonly IOptions<OutboxOptions> _options = Options.Create(new OutboxOptions
    {
        MaxRetryCount = 3,
        PublishTimeoutSeconds = 5,
        LeaseDurationSeconds = 10
    });

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
        var (uow, _, outbox, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow, msg);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(1);
        msg.ProcessedAtUtc.Should().NotBeNull();
        _transport.Verify(x => x.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RelayBatchAsync_TicketMergedEvent_PublishesThroughTransportAndMarksProcessed()
    {
        // Arrange
        var evt = new TicketMergedEvent(
            Guid.NewGuid(), "TKT-SOURCE", Guid.NewGuid(), Guid.NewGuid(), "TKT-MASTER", Guid.NewGuid());
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(TicketMergedEvent),
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow, msg);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(1);
        msg.ProcessedAtUtc.Should().NotBeNull();
        _transport.Verify(x => x.PublishAsync(It.IsAny<TicketMergedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RelayBatchAsync_TicketEscalatedEvent_PublishesThroughTransportAndMarksProcessed()
    {
        var evt = new TicketEscalatedEvent(Guid.NewGuid(), "TKT-ESC", 1, "SLA breached", null, "System");
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(TicketEscalatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow, msg);

        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        result.Published.Should().Be(1);
        msg.ProcessedAtUtc.Should().NotBeNull();
        _transport.Verify(x => x.PublishAsync(It.IsAny<TicketEscalatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RelayBatchAsync_ChatCreatedEvent_PublishesThroughTransportAndMarksProcessed()
    {
        var evt = new ChatCreatedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Nhân viên", "Nội dung thật",
            false, [], Guid.NewGuid(), Guid.NewGuid());
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(ChatCreatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            OccurredAtUtc = DateTime.UtcNow
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow, msg);

        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        result.Published.Should().Be(1);
        msg.ProcessedAtUtc.Should().NotBeNull();
        _transport.Verify(
            x => x.PublishAsync(It.IsAny<ChatCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RelayBatchAsync_NoPendingMessages_ReturnsEmptyResult()
    {
        // Arrange
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: Enumerable.Empty<OutboxMessage>());
        var sut = CreateSut(uow);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(0);
        result.Failed.Should().Be(0);
        _transport.Verify(x => x.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
        var (uow, _, outbox, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow, msg);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        msg.RetryCount.Should().Be(1);
        msg.LastError.Should().Contain("Unknown event type");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        var (uow, _, outbox, _, _, _, _) = MockTicketUnitOfWork.Build(outboxSeed: new[] { msg });
        var sut = CreateSut(uow);

        // Act
        var result = await sut.RelayBatchAsync(10, CancellationToken.None);

        // Assert
        result.Published.Should().Be(0);
        result.Failed.Should().Be(0); // Should not even be picked up
    }
    private OutboxRelayService CreateSut(Mock<TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork> uow,
        params OutboxMessage[] messages)
    {
        var claimService = new Mock<IOutboxClaimService>();
        var leaseOwner = new Mock<IOutboxLeaseOwner>();
        leaseOwner.SetupGet(owner => owner.Value).Returns("test-relay");

        foreach (var message in messages)
        {
            claimService
                .Setup(service => service.TryClaimAsync(message.Id, "test-relay", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(message);
            claimService
                .Setup(service => service.MarkProcessedAsync(message.Id, "test-relay", It.IsAny<CancellationToken>()))
                .Callback(() => message.ProcessedAtUtc = DateTime.UtcNow)
                .ReturnsAsync(true);
            claimService
                .Setup(service => service.MarkFailedAsync(message.Id, "test-relay", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, string, string, CancellationToken>((_, _, error, _) =>
                {
                    message.RetryCount += 1;
                    message.LastError = error;
                })
                .ReturnsAsync(true);
        }

        return new OutboxRelayService(
            uow.Object,
            claimService.Object,
            leaseOwner.Object,
            _transport.Object,
            _options,
            _logger.Object);
    }
}
