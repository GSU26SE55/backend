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
/// GH-768 — email xác nhận 2FA xuyên thiết bị không có consumer nào.
///
/// <para>
/// AuthService publish <c>SendTwoFactorCrossDeviceConfirmEmailEvent</c> từ #AUTH-51 và endpoint
/// trả 200, nhưng tìm khắp EmailService không có <c>IConsumer&lt;…&gt;</c> cho nó. Event vào Rabbit
/// rồi nằm đó; người dùng không nhận được link và không hoàn tất được trong TTL 10 phút — trong
/// khi mọi tầng đều báo thành công. Đây là kiểu hỏng khó lần nhất vì không có gì đỏ ở đâu cả.
/// </para>
/// </summary>
[Collection("EmailConsumerTests")]
public class SendTwoFactorCrossDeviceConfirmConsumerTests : IAsyncLifetime
{
    private ITestHarness _harness = null!;
    private IServiceProvider _provider = null!;
    private FakeHttpMessageHandler _fakeHandler = null!;
    private Mock<IEmailTemplateRenderer> _renderer = null!;
    private Mock<IInboxStore> _inbox = null!;
    private readonly List<(string Template, IReadOnlyDictionary<string, string?> Values)> _rendered = new();

    public async Task InitializeAsync()
    {
        _fakeHandler = new FakeHttpMessageHandler();

        _renderer = new Mock<IEmailTemplateRenderer>();
        _renderer.Setup(r => r.RenderAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, string?>, CancellationToken>(
                (t, v, _) => _rendered.Add((t, v)))
            .ReturnsAsync("<html>2FA CONFIRM HTML</html>");

        _inbox = new Mock<IInboxStore>();
        _inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailJet:ApiKey"] = "key",
                ["MailJet:ApiSecret"] = "secret",
                ["MailJet:FromEmail"] = "noreply@test.local",
                ["MailJet:DisplayName"] = "TestApp",
                ["MailJet:SendEndpoint"] = "https://fake.local/send",
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
        services.AddSingleton<IEmailProvider>(sp => sp.GetRequiredService<EmailSenderService>());

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<SendTwoFactorCrossDeviceConfirmConsumer>();
            // Xem ghi chú flaky trong SendAdminInviteConsumerTests — mặc định inactivity chỉ 1 giây.
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

    private static SendTwoFactorCrossDeviceConfirmEmailEvent NewEvent(
        string email = "user@example.com",
        string fullName = "Nguyễn Văn A",
        string url = "https://app.test.local/2fa/cross-device-confirm?token=abc123",
        int ttl = 10)
        => new(email, fullName, url, ttl);

    [Fact]
    public async Task Consume_RendersTemplate_AndSendsEmail()
    {
        var evt = NewEvent();

        await _harness.Bus.Publish(evt);

        (await _harness.Consumed.Any<SendTwoFactorCrossDeviceConfirmEmailEvent>()).Should().BeTrue(
            "trước bản sửa KHÔNG có consumer nào — event vào Rabbit rồi nằm đó");
        _fakeHandler.CallCount.Should().Be(1, "nhà cung cấp email phải được gọi đúng một lần");

        var (template, values) = _rendered.Should().ContainSingle().Subject;
        template.Should().Be(EmailTemplates.TwoFactorCrossDeviceConfirm);
        // URL đi thẳng từ event: AuthService là nơi DUY NHẤT dựng địa chỉ đích. Consumer tự ghép
        // lại sẽ tạo ra hai nguồn sự thật và link sai chỉ lộ ra khi người dùng bấm vào.
        values["ConfirmUrl"].Should().Be(evt.ConfirmUrl);
        values["ExpiresInMinutes"].Should().Be("10");
        values["Email"].Should().Be(evt.ToEmail);
        values["UserName"].Should().Be(evt.FullName);
    }

    [Fact]
    public async Task Consume_MissingFullName_FallsBackToEmail()
    {
        // Tài khoản Google OAuth chưa onboard có thể chưa có tên. Chào "Xin chào ," thì lem nhem,
        // nhưng đừng vì thế mà bỏ luôn email.
        await _harness.Bus.Publish(NewEvent(fullName: "   "));

        await _harness.Consumed.Any<SendTwoFactorCrossDeviceConfirmEmailEvent>();

        var (_, values) = _rendered.Should().ContainSingle().Subject;
        values["UserName"].Should().Be("user@example.com");
    }

    [Fact]
    public async Task Consume_DuplicateMessage_DoesNotSendTwice()
    {
        // Idempotency (tiêu chí nghiệm thu): message trùng thì KHÔNG được gửi lại email thứ hai.
        _inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        await _harness.Bus.Publish(NewEvent());
        await _harness.Consumed.Any<SendTwoFactorCrossDeviceConfirmEmailEvent>();

        _fakeHandler.CallCount.Should().Be(0);
        _rendered.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_ProviderFails_ReleasesInboxClaim_SoRetryCanRun()
    {
        // GH-764 — nhà cung cấp lỗi tạm thời không được biến thành mất email vĩnh viễn.
        _fakeHandler.ResponseStatus = System.Net.HttpStatusCode.InternalServerError;

        await _harness.Bus.Publish(NewEvent());
        await _harness.Consumed.Any<SendTwoFactorCrossDeviceConfirmEmailEvent>();

        _inbox.Verify(s => s.ReleaseAsync(
            It.IsAny<Guid>(), nameof(SendTwoFactorCrossDeviceConfirmConsumer), "gh764-test-token",
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _inbox.Verify(s => s.CompleteAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
