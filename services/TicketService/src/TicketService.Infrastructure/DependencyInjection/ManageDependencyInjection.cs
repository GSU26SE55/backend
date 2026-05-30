using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.DependencyInjection;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.BackgroundServices;
using TicketService.Infrastructure.Implements.Helpers;
using TicketService.Infrastructure.Implements.Repositories;
using TicketService.Infrastructure.Implements.Services;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Persistence.Seeders;

namespace TicketService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddRepositories();
        services.AddHelpers();
        services.AddOutbox();

        services.AddSharedInfrastructure(configuration, "TicketService.Application", "Ticket Service API");

        return services;
    }

    private static void AddOutbox(this IServiceCollection services)
    {
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        services.AddHostedService<OutboxRelayBackgroundService>();
    }

    private static void AddHelpers(this IServiceCollection services)
    {
        services.AddScoped<IPriorityCalculator, PriorityCalculator>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITicketCodeGenerator, TicketCodeGenerator>();
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TicketDb")
                               ?? configuration["TicketDb"]
                               ?? configuration["Ticket_Db"]
                               ?? configuration["Ticket_DB"]
                               ?? configuration["TICKET_DB"]
                               ?? configuration["TICKET_Db"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__TicketDb, TicketDb, Ticket_Db, Ticket_DB, TICKET_DB, or TICKET_Db.");

        services.AddDbContext<TicketDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetService<TicketDbContext>()!);
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ITicketUnitOfWork, UnitOfWork>();
        services.AddScoped<TicketDataSeeder>();
        services.AddHttpContextAccessor();
    }
}
