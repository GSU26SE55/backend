using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using EmailService.UnitTests.Helpers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.UnitTests.Consumers;

[Collection("EmailConsumerTests")]
public class SendEmailChangeOtpConsumerTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private IServiceProvider _provider = null!;
    private FakeHttpMessageHandler _fakeHandler = null!;
    private Mock<IEmailTemplateRenderer> _renderer = null!;
    private Mock<IInboxStore> _inbox = null!;

    public async Task InitializeAsync()
    {
        _fakeHandler = new FakeHttpMessageHandler();
        _renderer = new Mock<IEmailTemplateRenderer>();
        _renderer.Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("<html>EMAIL CHANGE HTML</html>");

        _inbox = new Mock<IInboxStore>();
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailJet:ApiKey"] = "key",
                ["MailJet:ApiSecret"] = "secret",
                ["MailJet:FromEmail"] = "noreply@test.local",
                ["MailJet:DisplayName"] = "TestApp",
                ["MailJet:SendEndpoint"] = "https://fake.local/send"
            }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(_renderer.Object);
        services.AddSingleton(_inbox.Object);
        services.AddSingleton(new HttpClient(_fakeHandler));
        services.AddSingleton<EmailSenderService>(sp => new EmailSenderService(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<HttpClient>()));

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<SendEmailChangeOtpConsumer>();
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
    public async Task Consume_RendersTemplate_AndSendsToNewEmail()
    {
        await _harness.Bus.Publish(new SendEmailChangeOtpEvent("new@example.com", "654321"));

        var consumerHarness = _harness.GetConsumerHarness<SendEmailChangeOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendEmailChangeOtpEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["Otp"] == "654321" &&
                d["UserName"] == "new@example.com" &&
                d["ExpireMinutes"] == "10"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequestBody.Should().Contain("new@example.com");
        _fakeHandler.LastRequestBody.Should().Contain("EMAIL CHANGE HTML");
    }

    [Fact]
    public async Task Consume_DuplicateMessage_InboxBlocks_NoEmailSent()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendEmailChangeOtpConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendEmailChangeOtpEvent("new@example.com", "111111"));

        await ConsumerTestWaiter.UntilAsync(
            () => _inbox.Verify(s => s.TryMarkProcessedAsync(
                It.IsAny<Guid>(),
                nameof(SendEmailChangeOtpConsumer),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce),
            TimeSpan.FromSeconds(10));

        _fakeHandler.CallCount.Should().Be(0);
        _renderer.Verify(r => r.RenderAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string?>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
