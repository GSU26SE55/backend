using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SmsService.Infrastructure.Consumers;
using SmsService.Infrastructure.Services;

namespace SmsService.UnitTests.Consumers;

public class SendPhoneOtpConsumerTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private IServiceProvider _provider = null!;
    private Mock<ISmsSender> _smsSender = null!;
    private Mock<IInboxStore> _inbox = null!;

    public async Task InitializeAsync()
    {
        _smsSender = new Mock<ISmsSender>();
        _smsSender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _inbox = new Mock<IInboxStore>();
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddSingleton(_smsSender.Object);
        services.AddSingleton(_inbox.Object);

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<SendPhoneOtpConsumer>();
        });

        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        if (_harness != null)
            await _harness.Stop();
        if (_provider is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else if (_provider is IDisposable d)
            d.Dispose();
    }

    [Fact]
    public async Task Consume_FirstTime_SendsSms()
    {
        await _harness.Bus.Publish(new SendPhoneOtpEvent("0901234567", "987654"));

        var consumerHarness = _harness.GetConsumerHarness<SendPhoneOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPhoneOtpEvent>()).Should().BeTrue();

        _smsSender.Verify(s => s.SendAsync(
            "0901234567",
            It.Is<string>(body => body.Contains("987654")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_DuplicateMessage_InboxBlocks_NoSmsSent()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendPhoneOtpConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendPhoneOtpEvent("0901234567", "987654"));

        var consumerHarness = _harness.GetConsumerHarness<SendPhoneOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPhoneOtpEvent>()).Should().BeTrue();

        _smsSender.Verify(s => s.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_SmsBodyContainsOtp_AndWarning()
    {
        await _harness.Bus.Publish(new SendPhoneOtpEvent("0901234567", "555666"));

        var consumerHarness = _harness.GetConsumerHarness<SendPhoneOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPhoneOtpEvent>()).Should().BeTrue();

        _smsSender.Verify(s => s.SendAsync(
            It.IsAny<string>(),
            It.Is<string>(body =>
                body.Contains("555666") &&
                body.Contains("khong chia se")),  // warning text
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
