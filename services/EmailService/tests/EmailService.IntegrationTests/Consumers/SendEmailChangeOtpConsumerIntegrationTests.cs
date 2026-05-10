using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Templates;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

[Collection("EmailServiceIntegration")]
public class SendEmailChangeOtpConsumerIntegrationTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public SendEmailChangeOtpConsumerIntegrationTests(EmailServiceFactory factory)
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
        _factory.Renderer.RenderResultFactory = (template, values)
            => $"<html data-template=\"{template}\">OTP={values.GetValueOrDefault("Otp")}</html>";
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_SendEmailChangeOtpEvent_RendersChangeTemplate_PostsToNewEmail()
    {
        var otp = $"EC{Random.Shared.Next(100000, 999999)}";
        var email = $"new-{Guid.NewGuid():N}@example.com";
        var evt = new SendEmailChangeOtpEvent(email, otp);

        await _harness.Bus.Publish(evt);

        await _factory.WaitForRenderCallAsync(otp);
        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().ContainSingle();
        renderCalls[0].TemplateName.Should().Be(EmailTemplates.OtpEmailChange);
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "UserName" && kvp.Value == email);

        // Email được gửi tới ToNewEmail.
        var mailReqs = _factory.MailjetHandler.Requests.Where(r => r.Body != null && r.Body.Contains(email)).ToList();
        mailReqs.Should().ContainSingle();
        mailReqs[0].Body.Should().Contain("Confirm new email");

        var consumerHarness = _harness.GetConsumerHarness<SendEmailChangeOtpConsumer>();
        (await consumerHarness.Consumed.Any<SendEmailChangeOtpEvent>()).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_TemplateMissing_FallsBackToOtpRegister()
    {
        var otp = $"ECFB{Random.Shared.Next(100000, 999999)}";
        var email = $"fallback-change-{Guid.NewGuid():N}@example.com";

        _factory.Renderer.RenderResultFactory = (template, values) =>
        {
            if (template == EmailTemplates.OtpEmailChange)
                throw new FileNotFoundException("template missing");
            return $"<html>OTP={values.GetValueOrDefault("Otp")}</html>";
        };

        var evt = new SendEmailChangeOtpEvent(email, otp);
        await _harness.Bus.Publish(evt);

        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().Contain(c => c.TemplateName == EmailTemplates.OtpEmailChange);
        renderCalls.Should().Contain(c => c.TemplateName == EmailTemplates.OtpRegister);

        var mailReqs = _factory.MailjetHandler.Requests.Where(r => r.Body != null && r.Body.Contains(email)).ToList();
        mailReqs.Should().ContainSingle();
    }
}
