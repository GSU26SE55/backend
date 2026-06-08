using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Services;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.BackgroundJobs;
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
        services.AddOutbox(configuration);

        services.AddSharedInfrastructure(configuration, "TicketService.Application", "Ticket Service API");
        services.AddInboxIdempotency(configuration);
        services.AddMessageBus(configuration, typeof(ManageDependencyInjection).Assembly, typeof(TicketService.Application.DependencyInjection.ManageDependencyInjection).Assembly);

        return services;
    }

    private static void AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IMessageProducerService, OutboxMessagePublisher>();
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        services.AddHostedService<OutboxRelayBackgroundService>();
        services.AddHostedService<SlaTimerBackgroundService>();
    }

    private static void AddHelpers(this IServiceCollection services)
    {
        services.AddScoped<IPriorityCalculator, PriorityCalculator>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<ITicketCodeGenerator, TicketCodeGenerator>();
        services.AddScoped<ISlaCalculator, SlaCalculator>();
        services.AddScoped<ISlaService, SlaService>();

        // Override CurrentUserService from Shared
        services.AddScoped<TicketCurrentUserService>();
        services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());
        services.AddScoped<ITicketCurrentUserService>(sp => sp.GetRequiredService<TicketCurrentUserService>());
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
