using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.Middleware;
using StackExchange.Redis;

namespace SharedInfrastructure.Idempotency;

public static class IdempotencyKeyExtensions
{
    /// <summary>
    /// Đăng ký Idempotency-Key middleware: IIdempotencyKeyStore + bind config "Idempotency".
    /// IConnectionMultiplexer cũng được register nếu chưa có (singleton, dùng connection string "Redis").
    /// </summary>
    public static IServiceCollection AddIdempotencyKey(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SectionName));

        // Idempotent: chỉ register IConnectionMultiplexer nếu chưa có service nào khác làm.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var connStr = configuration.GetConnectionString("Redis")
                          ?? configuration["Redis:ConnectionString"]
                          ?? "localhost:6379";
            return ConnectionMultiplexer.Connect(connStr);
        });
        services.AddSingleton<IIdempotencyKeyStore, RedisIdempotencyKeyStore>();
        return services;
    }

    public static IApplicationBuilder UseIdempotencyKey(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyKeyMiddleware>();
    }
}
