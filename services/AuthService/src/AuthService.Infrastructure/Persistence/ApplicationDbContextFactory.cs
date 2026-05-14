using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace AuthService.Infrastructure.Persistence;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        EnvFileLoader.LoadIfExists();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("services/AuthService/src/AuthService.Api/appsettings.json", optional: true)
            .AddJsonFile("services/AuthService/src/AuthService.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AuthDb")
                               ?? configuration["AuthDb"]
                               ?? "Host=localhost;Port=5432;Database=auth_db;Username=postgres;Password=Password12345@";

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
