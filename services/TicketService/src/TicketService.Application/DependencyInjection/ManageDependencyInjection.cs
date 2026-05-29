using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;

namespace TicketService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceApplication(this IServiceCollection services)
    {
        // Register MediatR handlers
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ManageDependencyInjection).Assembly));
        services.AddScoped<ITicketStateMachine, TicketStateMachine>();
        services.AddScoped<ITransitionRuleProvider, TransitionRuleProvider>();
        return services;
    }
}
