using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedInfrastructure.DependencyInjection.Extensions;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

public class CorsExtensionsTests
{
    [Fact]
    public void AddCorsExtentions_RegistersAllowAllPolicy_WithExpectedSettings()
    {
        var services = new ServiceCollection();

        services.AddCorsExtentions();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;

        var policy = options.GetPolicy("AllowAll");
        policy.Should().NotBeNull("AllowAll policy phải được register");

        // SetIsOriginAllowed(_ => true) → IsOriginAllowed delegate luôn true cho mọi origin.
        policy!.IsOriginAllowed("https://anything.example.com").Should().BeTrue();
        policy.IsOriginAllowed("http://localhost:1234").Should().BeTrue();

        policy.AllowAnyMethod.Should().BeTrue();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.SupportsCredentials.Should().BeTrue();
    }
}
