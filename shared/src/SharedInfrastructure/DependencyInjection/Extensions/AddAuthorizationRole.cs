using Microsoft.Extensions.DependencyInjection;

namespace SharedInfrastructure.DependencyInjection.Extensions;

public static class AddAuthorizationRole
{
    public static void AddRoleAuthorize(this IServiceCollection service)
    {
        /*service.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireClaim("Role", "1"));
            options.AddPolicy("UserOnly", policy => policy.RequireClaim("Role", "2"));
            options.AddPolicy("OrganizerOnly", policy => policy.RequireClaim("IsOrganizer", "true"));
            options.AddPolicy("AdminOrOrganizer", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == "Role" && c.Value == "1") ||
                    context.User.HasClaim(c => c.Type == "IsOrganizer" && c.Value == "true")
                ));
        });*/
    }
}
