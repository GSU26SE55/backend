using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;
using TicketService.Infrastructure.DependencyInjection;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.DependencyInjection;

public class TicketServiceOutboxRegistrationTests
{
    [Fact]
    [Obsolete]
    public void AddTicketServiceInfrastructure_RegistersTransactionalOutboxWriter()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TicketDb"] = "Host=localhost;Database=ticket;Username=test;Password=test",
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["JwtSettings:SecretKey"] = "test-secret-key-for-unit-test-only-32",
                ["JwtSettings:Issuer"] = "TicketService.Tests",
                ["JwtSettings:Audience"] = "TicketService.Tests"
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddTicketServiceInfrastructure(configuration);

        // Assert
        services.Last(x => x.ServiceType == typeof(IIntegrationEventOutboxWriter))
            .ImplementationType.Should().Be(typeof(IntegrationEventOutboxWriter));
    }
}
