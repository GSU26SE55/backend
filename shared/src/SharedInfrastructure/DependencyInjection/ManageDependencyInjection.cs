using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedContracts.Interfaces;
using SharedInfrastructure.Behaviors;
using SharedInfrastructure.Caching;
using SharedInfrastructure.DependencyInjection.Extensions;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Middleware;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Persistence.Repositories;
using SharedInfrastructure.Services;
using SharedInfrastructure.Swagger;
using SharedKernels.Interfaces;

namespace SharedInfrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration configuration, string assemblyName, string apiTitle)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddMediatRInfrastructure(configuration, assemblyName);
        // #AUTH-05 — truyền configuration + environment để CORS đọc whitelist `Cors:AllowedOrigins`.
        // `IHostEnvironment` lấy từ service đã đăng ký sẵn (WebApplicationBuilder luôn đăng ký).
        var hostEnv = services
            .FirstOrDefault(d => d.ServiceType == typeof(IHostEnvironment))?.ImplementationInstance
            as IHostEnvironment;
        services.AddCorsExtentions(configuration, hostEnv);
        services.AddJwtAuthentication(configuration);
        services.AddRoleAuthorize();
        services.AddCommonModelStateResponse();
        services.AddSharedSwaggerGen(apiTitle);

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });
        services.AddScoped<ICacheService, RedisCacheService>();

        // Sprint 7 #116 — OpenTelemetry tracing → Tempo (no-op nếu chưa cấu hình OtlpEndpoint).
        services.AddOpenTelemetryTracing(configuration, apiTitle);
        return services;
    }

    /// <summary>
    /// Wire pipeline middleware shared cho mọi service:
    /// 1. SecurityHeaders — set OWASP headers cho mọi response (đặt sớm để cả error response cũng có)
    /// 2. CorrelationId — sinh/forward X-Correlation-ID, push vào log scope
    /// 3. RequestLogging — log mọi HTTP request kèm duration + correlation ID
    /// 4. GlobalException — bắt unhandled exception, trả JSON CommonResponse chuẩn
    /// 5. CommonResponseStatusCodes — bọc response status-only (404/401/403/405/...) thành CommonResponse
    ///    để client luôn parse cùng schema, không gặp body rỗng khi route không tồn tại.
    /// Pipeline gọi method này TRƯỚC UseHttpsRedirection / UseCors / UseAuthentication.
    /// </summary>
    public static IApplicationBuilder UseSharedInfrastructure(this IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseCommonResponseStatusCodes();
        return app;
    }
}
