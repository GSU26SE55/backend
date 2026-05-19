using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using EmailService.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace EmailService.IntegrationTests.Host;

/// <summary>
/// Verify Program.cs wiring: HTTP host responds, MassTransit consumers + EmailSender + renderer registered.
/// </summary>
[Collection("EmailServiceIntegration")]
public class EmailServiceHostTests
{
    private readonly EmailServiceFactory _factory;

    public EmailServiceHostTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Root_ReturnsServiceRunningMessage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Email Service is Running");
    }

    [Fact]
    public void DI_RegistersAllConsumers_InAssemblyScan()
    {
        // Boot host trước khi access Services.
        _ = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Các consumer được resolve được = đã register qua AddConsumers(assembly).
        sp.GetService<SendAdminInviteConsumer>().Should().NotBeNull();
        sp.GetService<SendOtpRegisterConsumer>().Should().NotBeNull();
        sp.GetService<SendPasswordResetOtpConsumer>().Should().NotBeNull();
        sp.GetService<SendEmailChangeOtpConsumer>().Should().NotBeNull();
        sp.GetService<SendPhoneOtpConsumer>().Should().NotBeNull();
    }

    [Fact]
    public void DI_RegistersEmailSenderAndRenderer()
    {
        _ = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<EmailSenderService>().Should().NotBeNull("EmailSenderService phải được register qua AddHttpClient<>");
        sp.GetService<IEmailTemplateRenderer>().Should().NotBeNull("IEmailTemplateRenderer phải được register là Singleton");
    }

    [Fact]
    public void DI_BusControlIsRegistered()
    {
        _ = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetService<IBus>();
        var publisher = scope.ServiceProvider.GetService<IPublishEndpoint>();

        bus.Should().NotBeNull();
        publisher.Should().NotBeNull();
    }
}
