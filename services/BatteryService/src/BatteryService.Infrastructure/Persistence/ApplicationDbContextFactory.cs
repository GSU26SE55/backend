using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.Infrastructure.Persistence;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        EnvFileLoader.LoadIfExists();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("services/BatteryService/src/BatteryService.Api/appsettings.json", optional: true)
            .AddJsonFile("services/BatteryService/src/BatteryService.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("BatteryDb")
                               ?? configuration["BatteryDb"]
                               ?? "Host=localhost;Port=5432;Database=battery_db;Username=postgres;Password=Password12345@";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new DesignTimeCurrentUserService()));
    }

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}
