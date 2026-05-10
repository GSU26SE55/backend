using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Idempotency;

namespace SharedInfrastructure.UnitTests.Idempotency;

// Public để Moq DynamicProxy có thể tạo proxy (assembly MassTransit.Abstractions là strong-named).
public record IdempotencySampleEvent : IntegrationEvent
{
    public string Payload { get; set; } = string.Empty;
}

public class IdempotentConsumerExtensionsTests
{
    [Fact]
    public async Task ProcessOnceAsync_FirstCall_RunsAction_ReturnsTrue()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), "MyConsumer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent { Payload = "x" });
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var actionRan = 0;

        var ran = await ctxMock.Object.ProcessOnceAsync(inbox.Object, "MyConsumer", () =>
        {
            actionRan++;
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
        actionRan.Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnceAsync_DuplicateCall_SkipsAction_ReturnsFalse()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), "C", It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent());
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var actionRan = 0;
        var ran = await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C", () =>
        {
            actionRan++;
            return Task.CompletedTask;
        });

        ran.Should().BeFalse();
        actionRan.Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnceAsync_UsesIntegrationEventId_AsDedupeKey()
    {
        var evt = new IdempotencySampleEvent();

        var inbox = new Mock<IInboxStore>();
        Guid captured = Guid.Empty;
        inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback<Guid, string, CancellationToken>((id, _, _) => captured = id)
             .ReturnsAsync(true);

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(evt);
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C", () => Task.CompletedTask);

        captured.Should().Be(evt.Id);
    }

    [Fact]
    public async Task ProcessOnceAsync_ActionThrows_PropagatesException()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent());
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var act = async () => await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C",
            () => throw new InvalidOperationException("downstream error"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("downstream error");
    }
}
