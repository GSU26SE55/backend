using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SharedInfrastructure.Idempotency;

public static class IdempotencyServiceExtensions
{
    /// <summary>
    /// Đăng ký Inbox Pattern (chống consume duplicate). Phải gọi trong DI của service consumer.
    /// Cần config "Redis" connection string + section "Inbox".
    /// </summary>
    public static IServiceCollection AddInboxIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InboxOptions>(configuration.GetSection(InboxOptions.SectionName));
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connStr = configuration.GetConnectionString("Redis")
                          ?? configuration["Redis:ConnectionString"]
                          ?? "localhost:6379";
            return ConnectionMultiplexer.Connect(connStr);
        });
        services.AddScoped<IInboxStore, RedisInboxStore>();
        return services;
    }
}
