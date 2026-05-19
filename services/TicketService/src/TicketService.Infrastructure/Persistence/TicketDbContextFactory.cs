using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace TicketService.Infrastructure.Persistence;

public class TicketDbContextFactory : IDesignTimeDbContextFactory<TicketDbContext>
{
    public TicketDbContext CreateDbContext(string[] args)
    {
        EnvFileLoader.LoadIfExists();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("services/TicketService/src/TicketService.Api/appsettings.json", optional: true)
            .AddJsonFile("services/TicketService/src/TicketService.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("TicketDb")
                               ?? configuration["TicketDb"]
                               ?? "Host=localhost;Port=5432;Database=ticket_db;Username=postgres;Password=Password12345@";

        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TicketDbContext(
            options,
            new AuditableEntityInterceptor(new DesignTimeCurrentUserService()));
    }

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}
