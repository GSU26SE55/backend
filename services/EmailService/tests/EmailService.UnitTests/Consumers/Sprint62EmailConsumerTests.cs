using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using EmailService.UnitTests.Helpers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.UnitTests.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-02 (#673) + NOTI-04 (#675) — consumer email mới của EmailService.
/// </summary>
[Collection("EmailConsumerTests")]
public class Sprint62EmailConsumerTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private ServiceProvider _provider = null!;
    private FakeHttpMessageHandler _fakeHandler = null!;
    private Mock<IEmailTemplateRenderer> _renderer = null!;
    private Mock<IInboxStore> _inbox = null!;

    public async Task InitializeAsync()
    {
        _fakeHandler = new FakeHttpMessageHandler();

        _renderer = new Mock<IEmailTemplateRenderer>();
        _renderer.Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("<html>RENDERED</html>");

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
        services.AddSingleton(sp => new EmailSenderService(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<HttpClient>()));
        // Sprint 6.3 NOTI3-05 (#705) — consumer nay phụ thuộc IEmailProvider (seam cho provider thứ 2).
        services.AddSingleton<IEmailProvider>(sp => sp.GetRequiredService<EmailSenderService>());

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<SendNotificationEmailConsumer>();
            x.AddConsumer<SuspiciousLoginDetectedConsumer>();
            x.AddConsumer<RefreshTokenReuseDetectedConsumer>();
        });

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

    // ── NOTI-02 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotificationEmail_PlainTextBody_WrappedInGenericTemplate_AndSent()
    {
        await _harness.Bus.Publish(new SendNotificationEmailEvent(
            Guid.NewGuid(), "manager@example.com", "SLA đã breach", "Ticket TKT-1 quá hạn."));

        var consumer = _harness.GetConsumerHarness<SendNotificationEmailConsumer>();
        (await consumer.Consumed.Any<SendNotificationEmailEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.NotificationGeneric,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["Subject"] == "SLA đã breach" && d["Body"] == "Ticket TKT-1 quá hạn."),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequestBody.Should().Contain("manager@example.com");
        _fakeHandler.LastRequestBody.Should().Contain("RENDERED");
    }

    /// <summary>
    /// Body đã là HTML (consumer battery escalation / environmental incident render sẵn) thì gửi
    /// nguyên, không bọc lại template khác — nếu bọc, renderer HTML-encode sẽ hiện ra thẻ thô.
    /// </summary>
    [Fact]
    public async Task NotificationEmail_HtmlBody_SentAsIs_WithoutGenericTemplate()
    {
        await _harness.Bus.Publish(new SendNotificationEmailEvent(
            Guid.NewGuid(), "admin@example.com", "Escalation", "<div>PRE-RENDERED-MARKER</div>"));

        var consumer = _harness.GetConsumerHarness<SendNotificationEmailConsumer>();
        (await consumer.Consumed.Any<SendNotificationEmailEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.NotificationGeneric,
            It.IsAny<IReadOnlyDictionary<string, string?>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Mailjet payload escape ký tự non-ASCII và dấu '<' → assert bằng marker ASCII thuần,
        // đồng thời khẳng định KHÔNG dùng bản render "RENDERED" của template generic.
        _fakeHandler.LastRequestBody.Should().Contain("PRE-RENDERED-MARKER");
        _fakeHandler.LastRequestBody.Should().NotContain(">RENDERED<");
    }

    [Fact]
    public async Task NotificationEmail_MissingRecipient_DoesNotSend()
    {
        await _harness.Bus.Publish(new SendNotificationEmailEvent(Guid.NewGuid(), "", "S", "B"));

        var consumer = _harness.GetConsumerHarness<SendNotificationEmailConsumer>();
        (await consumer.Consumed.Any<SendNotificationEmailEvent>()).Should().BeTrue();

        _fakeHandler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotificationEmail_DuplicateMessage_InboxBlocks_NoEmailSent()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(
                It.IsAny<Guid>(), nameof(SendNotificationEmailConsumer), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendNotificationEmailEvent(
            Guid.NewGuid(), "dup@example.com", "S", "B"));

        var consumer = _harness.GetConsumerHarness<SendNotificationEmailConsumer>();
        (await consumer.Consumed.Any<SendNotificationEmailEvent>()).Should().BeTrue();

        _fakeHandler.CallCount.Should().Be(0);
    }

    // ── NOTI-04 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspiciousLogin_SendsAlertEmail_WithContextFields()
    {
        await _harness.Bus.Publish(new SuspiciousLoginDetectedEvent(
            Guid.NewGuid(), "victim@example.com", "203.0.113.9", "Chrome/1.0", "new_ip", DateTime.UtcNow));

        var consumer = _harness.GetConsumerHarness<SuspiciousLoginDetectedConsumer>();
        (await consumer.Consumed.Any<SuspiciousLoginDetectedEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.SuspiciousLogin,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["IpAddress"] == "203.0.113.9" &&
                d["UserAgent"] == "Chrome/1.0" &&
                d["Reason"]!.Contains("IP")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequestBody.Should().Contain("victim@example.com");
    }

    [Fact]
    public async Task SuspiciousLogin_NullIpAndUserAgent_UsesFallbackText()
    {
        await _harness.Bus.Publish(new SuspiciousLoginDetectedEvent(
            Guid.NewGuid(), "victim2@example.com", null, null, "new_user_agent", DateTime.UtcNow));

        var consumer = _harness.GetConsumerHarness<SuspiciousLoginDetectedConsumer>();
        (await consumer.Consumed.Any<SuspiciousLoginDetectedEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.SuspiciousLogin,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["IpAddress"] == "không xác định" && d["UserAgent"] == "không xác định"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SuspiciousLogin_MissingEmail_DoesNotSend()
    {
        await _harness.Bus.Publish(new SuspiciousLoginDetectedEvent(
            Guid.NewGuid(), "", "1.1.1.1", "UA", "new_ip", DateTime.UtcNow));

        var consumer = _harness.GetConsumerHarness<SuspiciousLoginDetectedConsumer>();
        (await consumer.Consumed.Any<SuspiciousLoginDetectedEvent>()).Should().BeTrue();

        _fakeHandler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RefreshTokenReuse_SendsAlertEmail_WithRevokedSessionCount()
    {
        await _harness.Bus.Publish(new RefreshTokenReuseDetectedEvent(
            Guid.NewGuid(), "victim3@example.com", Guid.NewGuid(), "198.51.100.7", "Firefox/2.0", DateTime.UtcNow, 4));

        var consumer = _harness.GetConsumerHarness<RefreshTokenReuseDetectedConsumer>();
        (await consumer.Consumed.Any<RefreshTokenReuseDetectedEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.RefreshTokenReuse,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["RevokedSessions"] == "4" && d["IpAddress"] == "198.51.100.7"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequestBody.Should().Contain("victim3@example.com");
    }

    [Fact]
    public async Task RefreshTokenReuse_MissingEmail_DoesNotSend()
    {
        await _harness.Bus.Publish(new RefreshTokenReuseDetectedEvent(
            Guid.NewGuid(), "", Guid.NewGuid(), null, null, DateTime.UtcNow, 1));

        var consumer = _harness.GetConsumerHarness<RefreshTokenReuseDetectedConsumer>();
        (await consumer.Consumed.Any<RefreshTokenReuseDetectedEvent>()).Should().BeTrue();

        _fakeHandler.CallCount.Should().Be(0);
    }
}
