using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Templates;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

[Collection("EmailServiceIntegration")]
public class SendPasswordResetOtpConsumerIntegrationTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public SendPasswordResetOtpConsumerIntegrationTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        _harness = await _factory.GetHarnessAsync();
    }

    public Task DisposeAsync()
    {
        // Reset render factory back tới default sau test fallback có thể đã set custom factory.
        _factory.Renderer.RenderResultFactory = (template, values)
            => $"<html data-template=\"{template}\">OTP={values.GetValueOrDefault("Otp")}</html>";
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_SendPasswordResetOtpEvent_RendersResetTemplate_PostsToMailjet()
    {
        var otp = $"PR{Random.Shared.Next(100000, 999999)}";
        var email = $"bob-{Guid.NewGuid():N}@example.com";
        var evt = new SendPasswordResetOtpEvent(email, otp);

        await _harness.Bus.Publish(evt);

        await _factory.WaitForRenderCallAsync(otp);
        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().ContainSingle();
        renderCalls[0].TemplateName.Should().Be(EmailTemplates.OtpPasswordReset);
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "ExpireMinutes" && kvp.Value == "10");

        var mailReqs = _factory.MailjetHandler.Requests.Where(r => r.Body != null && r.Body.Contains(email)).ToList();
        mailReqs.Should().ContainSingle();
        mailReqs[0].Body.Should().Contain("Reset password OTP");

        var consumerHarness = _harness.GetConsumerHarness<SendPasswordResetOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendPasswordResetOtpEvent>()).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_TemplateMissing_FallsBackToOtpRegister()
    {
        var otp = $"FB{Random.Shared.Next(100000, 999999)}";
        var email = $"fallback-{Guid.NewGuid():N}@example.com";

        // Setup: render call cho OtpPasswordReset throws FileNotFoundException → consumer fallback OtpRegister.
        _factory.Renderer.RenderResultFactory = (template, values) =>
        {
            if (template == EmailTemplates.OtpPasswordReset)
                throw new FileNotFoundException("template missing");
            return $"<html data-template=\"{template}\">OTP={values.GetValueOrDefault("Otp")}</html>";
        };

        var evt = new SendPasswordResetOtpEvent(email, otp);
        await _harness.Bus.Publish(evt);

        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().Contain(c => c.TemplateName == EmailTemplates.OtpPasswordReset);
        renderCalls.Should().Contain(c => c.TemplateName == EmailTemplates.OtpRegister);

        var mailReqs = _factory.MailjetHandler.Requests.Where(r => r.Body != null && r.Body.Contains(email)).ToList();
        mailReqs.Should().ContainSingle();
    }
}
