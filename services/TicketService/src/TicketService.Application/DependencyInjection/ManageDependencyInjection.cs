using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.Common.Models;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;

namespace TicketService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Options
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        // Register MediatR handlers
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ManageDependencyInjection).Assembly));
        services.AddScoped<ITicketStateMachine, TicketStateMachine>();
        services.AddScoped<ITransitionRuleProvider, TransitionRuleProvider>();
        return services;
    }
}
