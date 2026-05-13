using AuthService.Application.Authorization;
using AuthService.Application.Configuration;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Infrastructure.BackgroundJobs;
using AuthService.Infrastructure.Implements.Helpers;
using AuthService.Infrastructure.Implements.Repositories;
using AuthService.Infrastructure.Implements.Services;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;

namespace AuthService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddAuthServiceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScopedInterface(configuration);
        services.AddSharedInfrastructure(configuration, "AuthService.Application", "Auth Service API");

        services.AddMessageBus(configuration);

        // Outbox Pattern: override IMessageProducerService bằng OutboxMessagePublisher.
        // Handler publish event → INSERT vào DbContext.OutboxMessages, atomic với business data.
        // OutboxRelayBackgroundService poll bảng outbox và publish thật lên RabbitMQ.
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IMessageProducerService, OutboxMessagePublisher>();
        services.AddHostedService<OutboxRelayBackgroundService>();

        // Concurrent session limit policy. Bind từ section "Session" hoặc dùng default (5).
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));

        // Granular permission authorization:
        // [HasPermission("battery.view")] -> policy "perm:battery.view"
        // -> PermissionPolicyProvider tạo policy on-the-fly
        // -> PermissionAuthorizationHandler verify claim "perm".
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuthDb")
                               ?? configuration["AuthDb"]
                               ?? configuration["Auth_Db"]
                               ?? configuration["Auth_DB"]
                               ?? configuration["AUTH_DB"]
                               ?? configuration["AUTH_Db"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__AuthDb, AuthDb, Auth_Db, Auth_DB, AUTH_DB, or AUTH_Db.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetService<ApplicationDbContext>()!);
    }

    private static void AddScopedInterface(this IServiceCollection service, IConfiguration configuration)
    {
        service.AddScoped<IAuthUnitOfWork, UnitOfWork>();
        service.AddScoped<IJwtHelper, JwtHelper>();
        service.AddHttpClient<IGoogleOAuthHelper, GoogleOAuthHelper>();
        service.AddSingleton<IPasswordHasher, PasswordHasher>();
        service.AddScoped<AuthDataSeeder>();
        service.AddHttpContextAccessor();
    }
}
