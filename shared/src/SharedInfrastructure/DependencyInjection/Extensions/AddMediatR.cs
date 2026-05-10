using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedInfrastructure.DependencyInjection;

public static class AddMediatR
{
    public static void AddMediatRInfrastructure(this IServiceCollection service, IConfiguration config, string assemblyName)
    {
        var applicationAssembly = Assembly.Load(assemblyName);

        service.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
        });
    }
}
