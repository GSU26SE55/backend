using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SmsService.Application.Consumers;
using SmsService.Application.CQRS.Command.Sms;

namespace SmsService.UnitTests.Consumers;

public class SendSmsCommandConsumerTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private ServiceProvider _provider = null!;
    private Mock<IMediator> _mediator = null!;
    private Mock<IInboxStore> _inbox = null!;

    public async Task InitializeAsync()
    {
        _mediator = new Mock<IMediator>();
        _mediator
            .Setup(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<Guid> { IsSuccess = true, Data = Guid.NewGuid() });

        _inbox = new Mock<IInboxStore>();
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddSingleton(_mediator.Object);
        services.AddSingleton(_inbox.Object);
        services.AddMassTransitTestHarness(x => x.AddConsumer<SendSmsCommandConsumer>());

        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        if (_harness != null)
            await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Consume_FirstTime_ForwardsToQueueSmsCommand()
    {
        await _harness.Bus.Publish(new SendSmsCommand(
            PhoneNumber: "0901234567",
            Message: "Hello",
            SourceService: "auth",
            CorrelationId: Guid.NewGuid(),
            Category: "otp"));

        var consumerHarness = _harness.GetConsumerHarness<SendSmsCommandConsumer>();
        (await consumerHarness.Consumed.Any<SendSmsCommand>()).Should().BeTrue();

        _mediator.Verify(m => m.Send(
            It.Is<QueueSmsCommand>(q =>
                q.PhoneNumber == "0901234567" &&
                q.Message == "Hello" &&
                q.SourceService == "auth" &&
                q.Category == "otp"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_Duplicate_InboxBlocks_NoForward()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendSmsCommandConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendSmsCommand(
            "0901234567", "Hi", "auth", Guid.NewGuid()));

        var consumerHarness = _harness.GetConsumerHarness<SendSmsCommandConsumer>();
        (await consumerHarness.Consumed.Any<SendSmsCommand>()).Should().BeTrue();

        _mediator.Verify(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_QueueRejected_NoCrash_LogsWarning()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<Guid> { IsSuccess = false, StatusCode = 400, Message = "Phone invalid", Data = Guid.Empty });

        await _harness.Bus.Publish(new SendSmsCommand("xyz", "Hi", "auth", Guid.NewGuid()));

        var consumerHarness = _harness.GetConsumerHarness<SendSmsCommandConsumer>();
        (await consumerHarness.Consumed.Any<SendSmsCommand>()).Should().BeTrue();
        // Consumer KHÔNG throw — chỉ log warning. Verify mediator was still called.
        _mediator.Verify(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
