using System.Threading.RateLimiting;
using AuthService.Api.Extensions;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using Testcontainers.PostgreSql;

namespace AuthService.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory custom cho integration test — boot AuthService API trong process,
/// override DI để dùng:
/// - PostgreSQL Testcontainer (real DB, isolated từng test class)
/// - InMemory MassTransit (không cần RabbitMQ thật)
/// - InMemory distributed cache (không cần Redis thật)
/// - Stub IMessageProducerService capture event trong list (assert được)
/// - Stub IGoogleOAuthHelper trả mock GoogleUserInfo (không gọi Google thật)
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("auth_test")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    /// <summary>Capture events publish qua IMessageProducerService — assert trong test.</summary>
    public CapturingMessageProducer Producer { get; } = new();

    /// <summary>Stub Google OAuth — set ValidateAsyncResult/ExchangeResult trước khi test.</summary>
    public StubGoogleOAuthHelper GoogleOAuth { get; } = new();

    public string ConnectionString => _pgContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pgContainer.StartAsync();
        SetTestEnvironmentVariables();
    }

    public new async Task DisposeAsync()
    {
        ClearTestEnvironmentVariables();
        await _pgContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            // Inject test config — override mọi key environment có thể đã set sẵn từ .env.
            config.AddInMemoryCollection(CreateTestConfiguration());
        });

        builder.ConfigureServices(services =>
        {
            // 1. Strip MassTransit RabbitMQ — replace bằng InMemory bus (test harness).
            var massTransitDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                            && (d.ServiceType.FullName.StartsWith("MassTransit")
                                || d.ImplementationType?.FullName?.StartsWith("MassTransit") == true))
                .ToList();
            foreach (var d in massTransitDescriptors)
                services.Remove(d);

            // Loại bỏ luôn IHostedService của MassTransit để không cố start RabbitMQ.
            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType?.FullName?.Contains("MassTransit") == true))
                .ToList();
            foreach (var d in hostedServiceDescriptors)
                services.Remove(d);

            // Re-add MassTransit InMemory (no RabbitMQ).
            services.AddMassTransit(x =>
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            });

            // 2. Replace Redis distributed cache → InMemory distributed cache.
            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();

            // 3. Replace Redis idempotency store → InMemory store.
            services.RemoveAll<IIdempotencyKeyStore>();
            services.AddSingleton<IIdempotencyKeyStore, InMemoryIdempotencyKeyStore>();

            // 4. Replace IMessageProducerService với capture stub.
            services.RemoveAll<IMessageProducerService>();
            services.AddSingleton<IMessageProducerService>(Producer);

            // 5. Replace IGoogleOAuthHelper với stub.
            services.RemoveAll<IGoogleOAuthHelper>();
            services.AddSingleton<IGoogleOAuthHelper>(GoogleOAuth);

            // 6. Disable endpoint rate limits for integration tests.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(options =>
            {
                options.AddPolicy(RateLimitingExtensions.PolicyAnonOtp, _ =>
                    RateLimitPartition.GetNoLimiter("test-anon-otp"));
                options.AddPolicy(RateLimitingExtensions.PolicyAuthOtp, _ =>
                    RateLimitPartition.GetNoLimiter("test-auth-otp"));
            });

            // 7. Đảm bảo DB Test đã migrate xong khi factory build.
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.Migrate();
        });
    }

    /// <summary>Lấy DbContext mới (scope riêng) để inspect/seed DB từ test code.</summary>
    public ApplicationDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    /// <summary>Reset toàn bộ data trong DB (giữ nguyên schema) giữa các test.</summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE refresh_tokens, accounts, roles RESTART IDENTITY CASCADE;");
    }

    private Dictionary<string, string?> CreateTestConfiguration() => new()
    {
        ["ConnectionStrings:AuthDb"] = ConnectionString,
        ["ConnectionStrings:Redis"] = "localhost:9999", // dummy, sẽ remove Redis service ở dưới
        ["AuthDb"] = ConnectionString,
        // Match values với .env để token signing/validation consistent
        // bất chấp ASP.NET Core configuration provider priority.
        ["JwtSettings:SecretKey"] = "xynbqiydczvhbcomoewzcldjrqnwrwii",
        ["JwtSettings:Issuer"] = "https://localhost",
        ["JwtSettings:Audience"] = "https://localhost",
        ["RabbitMQ:Host"] = "localhost",
        ["RabbitMQ:Username"] = "guest",
        ["RabbitMQ:Password"] = "guest",
        ["GoogleOAuth:ClientId"] = "test-client-id.apps.googleusercontent.com",
        ["GoogleOAuth:ClientSecret"] = "test-client-secret",
        ["GoogleOAuth:RedirectUri"] = "https://test.local/api/auth/google/callback",
        ["GoogleOAuth:Scope"] = "openid email profile",
        ["AdminSeed:Email"] = "admin@test.local",
        ["AdminSeed:Password"] = "Admin123@"
    };

    private void SetTestEnvironmentVariables()
    {
        foreach (var (key, value) in CreateTestConfiguration())
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
    }

    private void ClearTestEnvironmentVariables()
    {
        foreach (var key in CreateTestConfiguration().Keys)
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), null);
    }
}

/// <summary>Capture mọi event publish — test có thể assert đã publish event nào, payload gì.</summary>
public class CapturingMessageProducer : IMessageProducerService
{
    public List<IntegrationEvent> Published { get; } = new();
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
    public void Clear() => Published.Clear();
}

public class InMemoryIdempotencyKeyStore : IIdempotencyKeyStore
{
    private readonly Dictionary<string, CachedIdempotencyResponse?> _entries = new();
    private readonly object _gate = new();

    public Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
                return Task.FromResult(false);

            _entries[key] = null;
            return Task.FromResult(true);
        }
    }

    public Task SaveResponseAsync(string key, int statusCode, string body, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _entries[key] = new CachedIdempotencyResponse(statusCode, body);
        }

        return Task.CompletedTask;
    }

    public Task<CachedIdempotencyResponse?> TryGetResponseAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.GetValueOrDefault(key));
        }
    }
}

/// <summary>Stub IGoogleOAuthHelper — test set ValidateResult / ExchangeResult / AuthorizationUrl trước.</summary>
public class StubGoogleOAuthHelper : IGoogleOAuthHelper
{
    public GoogleUserInfo? ValidateResult { get; set; }
    public string? ExchangeResult { get; set; }
    public string AuthorizationUrl { get; set; } = "https://accounts.google.com/stub";

    public Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
        => Task.FromResult(ValidateResult);

    public string BuildAuthorizationUrl(string state, string redirectUri)
        => $"{AuthorizationUrl}?state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public Task<string?> ExchangeCodeForIdTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
        => Task.FromResult(ExchangeResult);
}
