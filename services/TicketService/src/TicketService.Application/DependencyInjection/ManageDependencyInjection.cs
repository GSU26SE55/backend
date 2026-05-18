using Microsoft.Extensions.DependencyInjection;

namespace TicketService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceApplication(this IServiceCollection services)
    {
        // Register MediatR handlers
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ManageDependencyInjection).Assembly));

        return services;
    }
}
