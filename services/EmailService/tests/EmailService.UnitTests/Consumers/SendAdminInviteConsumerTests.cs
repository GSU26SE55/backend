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
public class SendAdminInviteConsumerTests : IAsyncLifetime
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
        _renderer.Setup(r => r.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html>ADMIN INVITE HTML</html>");

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
                ["MailJet:SendEndpoint"] = "https://fake.local/send",
                ["AdminInvite:AcceptUrlBase"] = "https://app.test.local/auth/accept-invite"
            })
            .Build();

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
            x.AddConsumer<SendAdminInviteConsumer>();

            // Sửa flaky 2026-07-31 — mặc định inactivity timeout của MassTransit v8 chỉ **1 giây**.
            // `harness.Consumed.Any<T>()` ngừng chờ khi bus "im" quá ngưỡng đó rồi trả `false`, nên
            // **hết giờ và hỏng thật cho ra CÙNG một kết quả**. Khi chạy cả solution song song
            // (~9 assembly cùng lúc) việc điều phối luồng trượt quá 1 giây là test đỏ dù code đúng —
            // chạy riêng assembly này thì pass trong 168ms. Nới trần theo đúng khuôn đã dùng ở
            // NotificationService (`Helpers/ConsumerTestHarness.cs`): hỏng chậm 30 giây vẫn tốt hơn
            // xanh-đỏ thất thường.
            x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
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
    public async Task Consume_RendersInviteTemplate_AndSendsViaMailjet()
    {
        var token = "token+/=needs-encoding";
        var evt = new SendAdminInviteEvent(
            Guid.NewGuid(),
            "invitee@example.com",
            "Invitee User",
            "Staff",
            token,
            new DateTime(2026, 5, 20, 8, 30, 0, DateTimeKind.Utc));

        await _harness.Bus.Publish(evt);

        var consumerHarness = _harness.GetConsumerHarness<SendAdminInviteConsumer>();
        (await consumerHarness.Consumed.Any<SendAdminInviteEvent>()).Should().BeTrue();

        _renderer.Verify(r => r.RenderAsync(
            EmailTemplates.AdminInvite,
            It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["UserName"] == "Invitee User" &&
                d["Email"] == "invitee@example.com" &&
                d["Role"] == "Staff" &&
                d["AcceptUrl"] == "https://app.test.local/auth/accept-invite?token=token%2B%2F%3Dneeds-encoding" &&
                d["ExpiresAt"] == "2026-05-20 08:30 UTC"),
            It.IsAny<CancellationToken>()), Times.Once);

        _fakeHandler.CallCount.Should().Be(1);
        _fakeHandler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        _fakeHandler.LastRequestBody.Should().Contain("invitee@example.com");
        _fakeHandler.LastRequestBody.Should().Contain("ADMIN INVITE HTML");
    }

    [Fact]
    public async Task Consume_DuplicateMessage_InboxBlocks_NoEmailSent()
    {
        _inbox.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), nameof(SendAdminInviteConsumer), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _harness.Bus.Publish(new SendAdminInviteEvent(
            Guid.NewGuid(),
            "invitee@example.com",
            "Invitee User",
            "Staff",
            "token",
            DateTime.UtcNow.AddHours(72)));

        await ConsumerTestWaiter.UntilAsync(
            () => _inbox.Verify(s => s.TryMarkProcessedAsync(
                It.IsAny<Guid>(),
                nameof(SendAdminInviteConsumer),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce),
            TimeSpan.FromSeconds(10));

        _fakeHandler.CallCount.Should().Be(0);
        _renderer.Verify(r => r.RenderAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string?>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
