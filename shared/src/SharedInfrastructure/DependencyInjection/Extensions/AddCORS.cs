using Microsoft.Extensions.DependencyInjection;

namespace SharedInfrastructure.DependencyInjection.Extensions;

public static class AddCORS
{
    public static IServiceCollection AddCorsExtentions(this IServiceCollection service)
    {
        service.AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                policy => policy.SetIsOriginAllowed(origin => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });
        return service;
    }
}
