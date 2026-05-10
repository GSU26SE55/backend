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
/// WebApplicationFactory cho EmailService — boot full Program.cs trong process,
/// sau đó override:
/// - MassTransit RabbitMQ → InMemory transport (kèm test harness để publish/inspect)
/// - HttpClient của EmailSenderService → FakeHttpMessageHandler (không gọi Mailjet thật)
/// - IEmailTemplateRenderer → RecordingTemplateRenderer (không cần file template thật)
/// Mục đích: verify wiring của Program.cs (assembly scan consumer, DI registration) hoạt động đúng end-to-end.
/// </summary>
public class EmailServiceFactory : WebApplicationFactory<Program>
{
    public FakeHttpMessageHandler MailjetHandler { get; } = new();
    public RecordingTemplateRenderer Renderer { get; } = new();

    private ITestHarness? _harness;
    private readonly SemaphoreSlim _harnessLock = new(1, 1);

    /// <summary>Lấy ITestHarness đã start — gọi nhiều lần vẫn an toàn (start 1 lần duy nhất).</summary>
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

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            // Inject test config — override những key MailJet/RabbitMQ.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailJet:ApiKey"] = "integration-test-key",
                ["MailJet:ApiSecret"] = "integration-test-secret",
                ["MailJet:FromEmail"] = "noreply@test.local",
                ["MailJet:DisplayName"] = "TestApp",
                ["MailJet:SendEndpoint"] = "https://fake.mailjet.local/v3.1/send",
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest"
            });
        });

        builder.ConfigureServices(services =>
        {
            // 1. Strip MassTransit (đã add RabbitMQ) → re-add với InMemory + TestHarness.
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

            // Re-add với TestHarness — register cùng 4 consumer như Program.cs (assembly của SendOtpRegisterConsumer).
            services.AddMassTransitTestHarness(x =>
            {
                x.AddConsumers(typeof(EmailService.Infrastructure.Consumers.SendOtpRegisterConsumer).Assembly);
            });

            // 2. Replace IEmailTemplateRenderer → RecordingTemplateRenderer (không cần đọc file thật).
            services.RemoveAll<IEmailTemplateRenderer>();
            services.AddSingleton<IEmailTemplateRenderer>(Renderer);

            // 3. Replace HttpClient của EmailSenderService → wired tới FakeHttpMessageHandler.
            // EmailSenderService được register qua AddHttpClient<>; thay primary handler.
            services.RemoveAll<EmailSenderService>();
            services.AddHttpClient<EmailSenderService>()
                .ConfigurePrimaryHttpMessageHandler(() => MailjetHandler);
        });
    }

    public void ResetCaptures()
    {
        MailjetHandler.Clear();
        Renderer.Clear();
    }

    /// <summary>Poll cho tới khi renderer được gọi với OTP cụ thể (proof rằng consumer đã chạy với event của test này).</summary>
    public async Task WaitForRenderCallAsync(string uniqueOtp, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Renderer.Calls.Any(c => c.Values.TryGetValue("Otp", out var v) && v == uniqueOtp))
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Renderer not called with Otp={uniqueOtp} within {timeoutMs}ms (calls={Renderer.Calls.Count}).");
    }

    /// <summary>Poll cho tới khi Mailjet handler nhận body chứa unique marker.</summary>
    public async Task WaitForMailjetCallAsync(string uniqueMarker, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (MailjetHandler.Requests.Any(r => r.Body != null && r.Body.Contains(uniqueMarker)))
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Mailjet not called with body containing '{uniqueMarker}' within {timeoutMs}ms (calls={MailjetHandler.CallCount}).");
    }

    /// <summary>Đếm Mailjet calls có body chứa marker — dùng để assert chính xác request của test này.</summary>
    public int CountMailjetCallsContaining(string marker)
        => MailjetHandler.Requests.Count(r => r.Body != null && r.Body.Contains(marker));

    /// <summary>Lấy render calls có Otp khớp marker.</summary>
    public IReadOnlyList<RenderCall> RenderCallsForOtp(string otp)
        => Renderer.Calls.Where(c => c.Values.TryGetValue("Otp", out var v) && v == otp).ToList();
}
