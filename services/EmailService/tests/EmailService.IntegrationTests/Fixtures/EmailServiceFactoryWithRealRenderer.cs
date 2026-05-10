using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// Variant của EmailServiceFactory: KHÔNG replace IEmailTemplateRenderer — dùng renderer file-based thật.
/// Test verify wiring renderer + file IO + placeholder regex hoạt động end-to-end qua bus.
/// Template được tạo runtime trong tempdir để không phụ thuộc wwwroot của project.
/// </summary>
public class EmailServiceFactoryWithRealRenderer : WebApplicationFactory<Program>, IDisposable
{
    public FakeHttpMessageHandler MailjetHandler { get; } = new();
    public string TemplateRoot { get; }

    private ITestHarness? _harness;
    private readonly SemaphoreSlim _harnessLock = new(1, 1);

    public EmailServiceFactoryWithRealRenderer()
    {
        TemplateRoot = Path.Combine(Path.GetTempPath(), $"integration-templates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TemplateRoot);

        // Setup các template thật:
        File.WriteAllText(Path.Combine(TemplateRoot, "OtpRegister.html"),
            "<html><body>Hello {{UserName}}, your code is {{Otp}} (expires {{ExpireMinutes}}m)</body></html>");
    }

    public async Task<ITestHarness> GetHarnessAsync()
    {
        if (_harness != null)
            return _harness;
        await _harnessLock.WaitAsync();
        try
        {
            if (_harness != null)
                return _harness;
            var h = Services.GetRequiredService<ITestHarness>();
            await h.Start();
            _harness = h;
            return h;
        }
        finally
        {
            _harnessLock.Release();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailJet:ApiKey"] = "test-key",
                ["MailJet:ApiSecret"] = "test-secret",
                ["MailJet:FromEmail"] = "noreply@test.local",
                ["MailJet:DisplayName"] = "TestApp",
                ["MailJet:SendEndpoint"] = "https://fake.mailjet.local/v3.1/send",
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                // Trỏ template root tới tempdir.
                ["EmailTemplates:Path"] = TemplateRoot,
                ["EmailTemplates:CacheDisabled"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Strip MassTransit RabbitMQ → InMemory.
            var massTransitDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                            && (d.ServiceType.FullName.StartsWith("MassTransit")
                                || d.ImplementationType?.FullName?.StartsWith("MassTransit") == true))
                .ToList();
            foreach (var d in massTransitDescriptors)
                services.Remove(d);

            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType?.FullName?.Contains("MassTransit") == true))
                .ToList();
            foreach (var d in hostedServiceDescriptors)
                services.Remove(d);

            services.AddMassTransitTestHarness(x =>
            {
                x.AddConsumers(typeof(EmailService.Infrastructure.Consumers.SendOtpRegisterConsumer).Assembly);
            });

            // KHÔNG replace IEmailTemplateRenderer → giữ EmailTemplateRenderer thật.

            // Replace HttpClient của EmailSenderService.
            services.RemoveAll<EmailSenderService>();
            services.AddHttpClient<EmailSenderService>()
                .ConfigurePrimaryHttpMessageHandler(() => MailjetHandler);
        });
    }

    public new void Dispose()
    {
        if (Directory.Exists(TemplateRoot))
            Directory.Delete(TemplateRoot, recursive: true);
        base.Dispose();
    }
}
