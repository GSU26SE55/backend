using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Infrastructure.BackgroundJobs;
using AuthService.Infrastructure.Implements.Helpers;
using AuthService.Infrastructure.Implements.Repositories;
using AuthService.Infrastructure.Implements.Services;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
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
                "Missing connection string. Expected ConnectionStrings__DefaultConnection, DEFAULT_CONNECTION, POSTGRES_CONNECTION_STRING, DATABASE_URL, or ConnectionStrings:DefaultConnection.");

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
