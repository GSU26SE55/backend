using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace SmsService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory cho <c>dotnet ef migrations / database update</c>.
/// Path tương đối giả định chạy lệnh từ REPO ROOT (<c>capstone/backend/</c>) — khớp pattern AuthService.
/// </summary>
public class SmsDbContextFactory : IDesignTimeDbContextFactory<SmsDbContext>
{
    public SmsDbContext CreateDbContext(string[] args)
    {
        EnvFileLoader.LoadIfExists();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("services/SmsService/src/SmsService.Api/appsettings.json", optional: true)
            .AddJsonFile("services/SmsService/src/SmsService.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SmsDb")
                               ?? configuration["SmsDb"]
                               ?? configuration["Sms_Db"]
                               ?? configuration["SMS_DB"]
                               ?? "Host=localhost;Port=5432;Database=sms_db;Username=postgres;Password=Password12345@";

        var options = new DbContextOptionsBuilder<SmsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SmsDbContext(
            options,
            new AuditableEntityInterceptor(new DesignTimeCurrentUserService()));
    }

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}
