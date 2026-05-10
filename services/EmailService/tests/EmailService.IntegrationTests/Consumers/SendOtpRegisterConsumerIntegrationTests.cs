using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Templates;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// End-to-end: publish SendOtpRegisterEvent qua bus thật (InMemory),
/// verify SendOtpRegisterConsumer (registered qua assembly scan trong Program.cs) chạy,
/// render đúng template, và POST tới Mailjet với payload đúng.
/// Mỗi test dùng OTP unique để tách biệt với captures từ test khác cùng harness.
/// </summary>
[Collection("EmailServiceIntegration")]
public class SendOtpRegisterConsumerIntegrationTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public SendOtpRegisterConsumerIntegrationTests(EmailServiceFactory factory)
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
    public async Task Publish_SendOtpRegisterEvent_ConsumerRuns_RendersTemplate_PostsToMailjet()
    {
        var otp = $"REG{Random.Shared.Next(100000, 999999)}";
        var email = $"alice-{Guid.NewGuid():N}@example.com";
        var evt = new SendOtpRegisterEvent(email, otp);

        await _harness.Bus.Publish(evt);

        await _factory.WaitForRenderCallAsync(otp);
        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().ContainSingle();
        renderCalls[0].TemplateName.Should().Be(EmailTemplates.OtpRegister);
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "UserName" && kvp.Value == email);

        // Có duy nhất 1 Mailjet request cho email này.
        var mailReqs = _factory.MailjetHandler.Requests.Where(r => r.Body != null && r.Body.Contains(email)).ToList();
        mailReqs.Should().ContainSingle();
        mailReqs[0].Method.Should().Be(HttpMethod.Post);
        mailReqs[0].Uri!.ToString().Should().Be("https://fake.mailjet.local/v3.1/send");
        mailReqs[0].AuthorizationScheme.Should().Be("Basic");
        mailReqs[0].Body.Should().Contain(otp);

        // Verify consumer harness saw it.
        var consumerHarness = _harness.GetConsumerHarness<SendOtpRegisterConsumer>();
        (await consumerHarness.Consumed.Any<SendOtpRegisterEvent>()).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_EmptyEmail_RendererCalledWithBạnFallback_NoMailjetCallSucceeds()
    {
        var otp = $"EMPTY{Random.Shared.Next(100000, 999999)}";
        var evt = new SendOtpRegisterEvent(string.Empty, otp);

        await _harness.Bus.Publish(evt);

        // Renderer vẫn được gọi (consumer set UserName="bạn"), kể cả khi sender throw.
        await _factory.WaitForRenderCallAsync(otp);

        var renderCalls = _factory.RenderCallsForOtp(otp);
        renderCalls.Should().ContainSingle();
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "UserName" && kvp.Value == "bạn");
    }
}
