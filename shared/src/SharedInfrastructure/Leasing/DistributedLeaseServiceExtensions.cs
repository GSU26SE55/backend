using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SharedInfrastructure.Leasing;

public static class DistributedLeaseServiceExtensions
{
    /// <summary>
    /// GH-793 — đăng ký <see cref="IDistributedLease"/> (quyền chạy độc quyền trên Redis).
    /// </summary>
    /// <remarks>
    /// Dùng <c>TryAdd</c> cho <see cref="IConnectionMultiplexer"/> vì nhiều service đã đăng ký nó
    /// qua <c>AddInboxIdempotency</c>: đăng ký đè sẽ tạo thêm một kết nối Redis thứ hai cho cùng một
    /// tiến trình, tốn tài nguyên mà không ai để ý.
    /// </remarks>
    public static IServiceCollection AddDistributedLease(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connStr = configuration.GetConnectionString("Redis")
                          ?? configuration["Redis:ConnectionString"]
                          ?? "localhost:6379";
            return ConnectionMultiplexer.Connect(connStr);
        });

        services.TryAddSingleton<IDistributedLease, RedisDistributedLease>();
        return services;
    }
}
