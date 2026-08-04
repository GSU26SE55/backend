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
public class SendOtpRegisterConsumerTests : IAsyncLifetime
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
                 .ReturnsAsync("<html>OTP HTML</html>");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailJet:ApiKey"] = "test-key",
                ["MailJet:ApiSecret"] = "test-secret",
                ["MailJet:FromEmail"] = "noreply@test.local",
                ["MailJet:DisplayName"] = "TestApp",
                ["MailJet:SendEndpoint"] = "https://fake.mailjet.local/v3.1/send"
            })
            .Build();

        _inbox = new Mock<IInboxStore>();
        // Default: lần đầu xử lý → true
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(_renderer.Object);
        services.AddSingleton(_inbox.Object);
        services.AddSingleton(new HttpClient(_fakeHandler));
        services.AddSingleton<EmailSenderService>(sp => new EmailSenderService(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<HttpClient>()));
        // Sprint 6.3 NOTI3-05 (#705) — consumer nay phụ thuộc IEmailProvider (seam cho provider thứ 2).
        services.AddSingleton<IEmailProvider>(sp => sp.GetRequiredService<EmailSenderService>());

        services.AddMassTransitTestHarness(x =>
        {
            // Flaky guard 2026-07-31: inactivity mặc định của MassTransit v8 = 1s ⇒ Consumed.Any<T>()
            // trả false khi cả solution chạy song song. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs
            x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            x.AddConsumer<SendOtpRegisterConsumer>();
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
    public async Task Consume_RendersTemplate_AndSendsViaMailjet()
    {
        var evt = new SendOtpRegisterEvent("user@example.com", "123456");
        await _harness.Bus.Publish(evt);

        (await _harness.Consumed.Any<SendOtpRegisterEvent>()).Should().BeTrue();

        var consumerHarness = _harness.GetConsumerHarness<SendOtpRegisterConsumer>();
        (await consumerHarness.Consumed.Any<SendOtpRegisterEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.OtpRegister,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["Otp"] == "123456" &&
                d["UserName"] == "user@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        _fakeHandler.LastRequestBody.Should().Contain("user@example.com");
        _fakeHandler.LastRequestBody.Should().Contain("OTP HTML");
    }

    [Fact]
    public async Task Consume_DuplicateMessage_InboxBlocks_NoEmailSent()
    {
        // F003: nếu Inbox báo "đã processed" → consumer skip, không gọi Mailjet
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendOtpRegisterConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var evt = new SendOtpRegisterEvent("user@example.com", "123456");
        await _harness.Bus.Publish(evt);

        // Inbox PHẢI được hỏi (consumer đã chạy)
        await ConsumerTestWaiter.UntilAsync(
            () => _inbox.Verify(s => s.TryMarkProcessedAsync(
                It.IsAny<Guid>(),
                nameof(SendOtpRegisterConsumer),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce),
            TimeSpan.FromSeconds(10));

        // Mailjet KHÔNG được gọi
        _fakeHandler.CallCount.Should().Be(0);
        // Template renderer cũng không được gọi (skip toàn bộ logic gửi)
        _renderer.Verify(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_SameMessageId_5Times_OnlyFirstSendsEmail()
    {
        var sequence = new Queue<bool>(new[] { true, false, false, false, false });
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendOtpRegisterConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => sequence.Count > 0 ? sequence.Dequeue() : false);

        var evt = new SendOtpRegisterEvent("user@example.com", "999999");

        for (int i = 0; i < 5; i++)
            await _harness.Bus.Publish(evt);

        var consumerHarness = _harness.GetConsumerHarness<SendOtpRegisterConsumer>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (consumerHarness.Consumed.Select<SendOtpRegisterEvent>().Count() >= 5)
                break;
            await Task.Delay(100);
        }

        _inbox.Verify(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendOtpRegisterConsumer), It.IsAny<CancellationToken>()),
            Times.AtLeast(5));

        // Mailjet chỉ gọi 1 lần (4 lần sau bị Inbox skip)
        _fakeHandler.CallCount.Should().Be(1);
    }
}
