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

/// <summary>
/// Backward-compat consumer cho AuthService cũ — sau khi AuthService migrate (Phase 9)
/// + verify 1-2 sprint không còn event, sẽ xóa consumer này. Tests đảm bảo behavior:
/// (1) Render template OTP body chuẩn, (2) Forward QueueSmsCommand với source="auth"+category="otp".
/// </summary>
public class SendPhoneOtpConsumerTests : IAsyncLifetime
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
        services.AddMassTransitTestHarness(x => x.AddConsumer<SendPhoneOtpConsumer>());

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
    public async Task Consume_RendersOtpBody_AndForwardsAsOtpCategory()
    {
        await _harness.Bus.Publish(new SendPhoneOtpEvent("0901234567", "987654"));

        var consumerHarness = _harness.GetConsumerHarness<SendPhoneOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPhoneOtpEvent>()).Should().BeTrue();

        _mediator.Verify(m => m.Send(
            It.Is<QueueSmsCommand>(q =>
                q.PhoneNumber == "0901234567" &&
                q.Message.Contains("987654") &&
                q.Message.Contains("khong chia se") &&
                q.SourceService == "auth" &&
                q.Category == "otp"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_DuplicateMessage_InboxBlocks_NoForward()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendPhoneOtpConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendPhoneOtpEvent("0901234567", "987654"));

        var consumerHarness = _harness.GetConsumerHarness<SendPhoneOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPhoneOtpEvent>()).Should().BeTrue();

        _mediator.Verify(m => m.Send(It.IsAny<QueueSmsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
