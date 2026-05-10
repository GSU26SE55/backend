using System.Text.Json;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// Test với EmailTemplateRenderer THẬT (file IO + placeholder regex):
/// publish event → renderer load file → consumer build payload Mailjet với HTML đã render.
/// </summary>
public class RealRendererIntegrationTests : IClassFixture<EmailServiceFactoryWithRealRenderer>, IAsyncLifetime
{
    private readonly EmailServiceFactoryWithRealRenderer _factory;
    private ITestHarness _harness = null!;

    public RealRendererIntegrationTests(EmailServiceFactoryWithRealRenderer factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        _harness = await _factory.GetHarnessAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_RegisterEvent_RealRendererProducesExpandedHtml_InMailjetBody()
    {
        var email = $"real-{Guid.NewGuid():N}@x.com";
        var otp = $"REAL{Random.Shared.Next(100000, 999999)}";

        await _harness.Bus.Publish(new SendOtpRegisterEvent(email, otp));

        // Đợi Mailjet được gọi với marker email.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_factory.MailjetHandler.Requests.Any(r => r.Body != null && r.Body.Contains(email)))
                break;
            await Task.Delay(50);
        }

        var req = _factory.MailjetHandler.Requests.FirstOrDefault(r => r.Body != null && r.Body.Contains(email));
        req.Should().NotBeNull("Mailjet phải nhận request cho email này");

        // Parse Mailjet payload, extract HTMLPart, verify placeholders đã thay.
        var doc = JsonDocument.Parse(req!.Body!);
        var htmlPart = doc.RootElement.GetProperty("Messages")[0].GetProperty("HTMLPart").GetString();
        htmlPart.Should().NotBeNullOrEmpty();
        htmlPart.Should().Contain($"Hello {email}");
        htmlPart.Should().Contain($"your code is {otp}");
        htmlPart.Should().Contain("expires 5m");

        // Đảm bảo placeholder không leak (chuỗi {{...}} không còn trong output).
        htmlPart.Should().NotContain("{{");
        htmlPart.Should().NotContain("}}");
    }

    [Fact]
    public async Task Publish_PasswordResetEvent_FallsBackToOtpRegisterTemplate()
    {
        // Test factory chỉ có file OtpRegister.html → consumer fallback từ OtpPasswordReset → OtpRegister.
        var email = $"pw-fallback-{Guid.NewGuid():N}@x.com";
        var otp = $"PWFB{Random.Shared.Next(100000, 999999)}";

        await _harness.Bus.Publish(new SendPasswordResetOtpEvent(email, otp));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_factory.MailjetHandler.Requests.Any(r => r.Body != null && r.Body.Contains(email)))
                break;
            await Task.Delay(50);
        }

        var req = _factory.MailjetHandler.Requests.FirstOrDefault(r => r.Body != null && r.Body.Contains(email));
        req.Should().NotBeNull();

        var doc = JsonDocument.Parse(req!.Body!);
        var html = doc.RootElement.GetProperty("Messages")[0].GetProperty("HTMLPart").GetString();
        html.Should().Contain(otp);
        html.Should().Contain($"Hello {email}");
        // Subject vẫn là "Reset password OTP" (consumer set), không phải register subject.
        var subject = doc.RootElement.GetProperty("Messages")[0].GetProperty("Subject").GetString();
        subject.Should().Contain("Reset password OTP");
    }
}
