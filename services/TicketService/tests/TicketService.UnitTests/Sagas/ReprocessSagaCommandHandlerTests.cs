using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedInfrastructure.Idempotency;
using TicketService.Application.CQRS.Command.Sagas;
using TicketService.Application.CQRS.Handler.Sagas;
using TicketService.Application.Interfaces.Services;

namespace TicketService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #239 — ReprocessSagaCommandHandler with Idempotency-Key semantics.
/// </summary>
public class ReprocessSagaCommandHandlerTests
{
    private static ReprocessSagaCommandHandler Build(
        Mock<IAlertTicketSagaQueryService> sagaQuery,
        Mock<IIdempotencyKeyStore> idempotency)
        => new(sagaQuery.Object, idempotency.Object, NullLogger<ReprocessSagaCommandHandler>.Instance);

    [Fact]
    public async Task DuplicateIdempotencyKey_ShouldReturn200Idempotent_NotResetState()
    {
        var alertId = Guid.NewGuid();
        var sagaQuery = new Mock<IAlertTicketSagaQueryService>();
        var idempotency = new Mock<IIdempotencyKeyStore>();
        idempotency.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // already reserved

        var handler = Build(sagaQuery, idempotency);

        var result = await handler.Handle(new ReprocessSagaCommand
        {
            AlertId = alertId,
            IdempotencyKey = "abc",
            PerformedBy = Guid.NewGuid()
        }, default);

        result.StatusCode.Should().Be(200);
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("Idempotent");

        sagaQuery.Verify(s => s.ResetFailedStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NotInFailedState_ShouldReturn409Conflict()
    {
        var alertId = Guid.NewGuid();
        var sagaQuery = new Mock<IAlertTicketSagaQueryService>();
        sagaQuery.Setup(s => s.ResetFailedStateAsync(alertId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // saga not Failed
        var idempotency = new Mock<IIdempotencyKeyStore>();
        idempotency.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = Build(sagaQuery, idempotency);

        var result = await handler.Handle(new ReprocessSagaCommand
        {
            AlertId = alertId,
            IdempotencyKey = "abc",
            PerformedBy = Guid.NewGuid()
        }, default);

        result.StatusCode.Should().Be(409);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ValidFailedSaga_ShouldReturn202_ResetCalled()
    {
        var alertId = Guid.NewGuid();
        var performedBy = Guid.NewGuid();
        var sagaQuery = new Mock<IAlertTicketSagaQueryService>();
        sagaQuery.Setup(s => s.ResetFailedStateAsync(alertId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var idempotency = new Mock<IIdempotencyKeyStore>();
        idempotency.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = Build(sagaQuery, idempotency);

        var result = await handler.Handle(new ReprocessSagaCommand
        {
            AlertId = alertId,
            IdempotencyKey = "abc",
            PerformedBy = performedBy
        }, default);

        result.StatusCode.Should().Be(202);
        result.IsSuccess.Should().BeTrue();
        sagaQuery.Verify(s => s.ResetFailedStateAsync(alertId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdempotencyKeyScopedPerAlertId_ShouldUseAlertIdInKey()
    {
        var alertId = Guid.NewGuid();
        var capturedKey = "";
        var sagaQuery = new Mock<IAlertTicketSagaQueryService>();
        sagaQuery.Setup(s => s.ResetFailedStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var idempotency = new Mock<IIdempotencyKeyStore>();
        idempotency.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan, CancellationToken>((key, _, _) => capturedKey = key)
            .ReturnsAsync(true);

        var handler = Build(sagaQuery, idempotency);

        await handler.Handle(new ReprocessSagaCommand
        {
            AlertId = alertId,
            IdempotencyKey = "my-key-123",
            PerformedBy = Guid.NewGuid()
        }, default);

        capturedKey.Should().Contain(alertId.ToString());
        capturedKey.Should().Contain("my-key-123");
    }

    [Fact]
    public async Task ValidationFails_WhenIdempotencyKeyMissing()
    {
        var cmd = new ReprocessSagaCommand
        {
            AlertId = Guid.NewGuid(),
            IdempotencyKey = "",
            PerformedBy = Guid.NewGuid()
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(e => e.Field == nameof(ReprocessSagaCommand.IdempotencyKey));
    }

    [Fact]
    public async Task ValidationFails_WhenAlertIdEmpty()
    {
        var cmd = new ReprocessSagaCommand
        {
            AlertId = Guid.Empty,
            IdempotencyKey = "abc",
            PerformedBy = Guid.NewGuid()
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == nameof(ReprocessSagaCommand.AlertId));
    }
}
