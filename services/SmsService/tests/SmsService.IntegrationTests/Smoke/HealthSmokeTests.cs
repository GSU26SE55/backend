using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;

namespace SmsService.IntegrationTests.Smoke;

/// <summary>
/// Smoke test cấp project — verify DI graph của <c>SmsService.Api</c> wire đúng,
/// không cần khởi động RabbitMQ/Redis/Postgres thật. Integration test cấp E2E
/// (queue → SignalR → SIM) document trong README + §68 Phụ lục C (cần SIM thật).
/// </summary>
public class DiGraphTests : IClassFixture<SmsApiFactory>
{
    private readonly SmsApiFactory _factory;

    public DiGraphTests(SmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void DiGraph_ResolvesAllCriticalServices()
    {
        // Skip nếu env không có RabbitMQ/Redis — chỉ smoke verify DI graph build được.
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "1")
            return;

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<ISmsUnitOfWork>().Should().NotBeNull();
        sp.GetRequiredService<ISmsGatewayNotifier>().Should().NotBeNull();
        sp.GetRequiredService<IGatewayApiKeyHasher>().Should().NotBeNull();
    }
}

/// <summary>
/// Custom <see cref="WebApplicationFactory{TProgram}"/> để boot SmsService.Api trong test.
/// Build server không có RabbitMQ/Postgres thật — chỉ verify Program.cs không throw lúc startup.
/// E2E test thật (#SMS-42) cần stack đầy đủ + Flutter app + SIM, không chạy ở đây.
/// </summary>
public class SmsApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Tắt hạn mức nền: integration test bắn hàng loạt request trong vài giây, dính trần
        // 60 req/30s của bậc ẩn danh sẽ đỏ vì 429 chứ không phải vì logic sai.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "false"
            }));
        return base.CreateHost(builder);
    }
}
