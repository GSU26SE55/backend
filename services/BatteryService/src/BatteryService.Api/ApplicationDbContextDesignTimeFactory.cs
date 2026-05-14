using BatteryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.Api;

public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        EnvFileLoader.LoadIfExists();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BatteryDb")
                               ?? Environment.GetEnvironmentVariable("BatteryDb")
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
