using BatteryService.Application.CQRS.Command.BatteryType;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddBatteryApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateBatteryTypeCommand).Assembly));
        return services;
    }
}
