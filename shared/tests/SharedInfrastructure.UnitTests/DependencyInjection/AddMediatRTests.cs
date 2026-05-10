using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.DependencyInjection;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

public class AddMediatRTests
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddMediatRInfrastructure_RegistersIMediator_AsTransient()
    {
        var services = new ServiceCollection();

        services.AddMediatRInfrastructure(Empty(), assemblyName: "SharedInfrastructure");

        var d = services.FirstOrDefault(x => x.ServiceType == typeof(IMediator));
        d.Should().NotBeNull("AddMediatR phải register IMediator");
        d!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddMediatRInfrastructure_RegistersISender_AndIPublisher()
    {
        var services = new ServiceCollection();

        services.AddMediatRInfrastructure(Empty(), assemblyName: "SharedInfrastructure");

        services.Should().Contain(d => d.ServiceType == typeof(ISender));
        services.Should().Contain(d => d.ServiceType == typeof(IPublisher));
    }

    [Fact]
    public void AddMediatRInfrastructure_AssemblyNotFound_ThrowsFileNotFound()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMediatRInfrastructure(Empty(), assemblyName: "Definitely.Does.Not.Exist");

        act.Should().Throw<System.IO.FileNotFoundException>();
    }

    [Fact]
    public void AddMediatRInfrastructure_CanResolveIMediator_FromBuiltProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatRInfrastructure(Empty(), assemblyName: "SharedInfrastructure");

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetService<IMediator>();

        mediator.Should().NotBeNull();
    }
}
