using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;
using SharedInfrastructure.Behaviors;
using SharedInfrastructure.Caching;
using SharedInfrastructure.DependencyInjection.Extensions;
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
        services.AddCorsExtentions();
        services.AddJwtAuthentication(configuration);
        services.AddRoleAuthorize();
        services.AddSharedSwaggerGen(apiTitle);

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });
        services.AddScoped<ICacheService, RedisCacheService>();
        return services;
    }

    /// <summary>
    /// Wire pipeline middleware shared cho mọi service:
    /// 1. SecurityHeaders — set OWASP headers cho mọi response (đặt sớm để cả error response cũng có)
    /// 2. CorrelationId — sinh/forward X-Correlation-ID, push vào log scope
    /// 3. RequestLogging — log mọi HTTP request kèm duration + correlation ID
    /// 4. GlobalException — bắt unhandled exception, trả JSON chuẩn
    /// Pipeline gọi method này TRƯỚC UseHttpsRedirection / UseCors / UseAuthentication.
    /// </summary>
    public static IApplicationBuilder UseSharedInfrastructure(this IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        return app;
    }
}
