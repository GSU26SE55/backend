using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditAggregatorService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory cho <c>dotnet ef migrations</c> (Sprint audit <c>#AUDIT-14</c>).
/// Lấy connection string từ env <c>AuditAggregatorDb</c>; fallback placeholder khi chỉ generate migration (không cần connect).
/// </summary>
public class AuditAggregateDbContextFactory : IDesignTimeDbContextFactory<AuditAggregateDbContext>
{
    public AuditAggregateDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__AuditAggregatorDb")
            ?? Environment.GetEnvironmentVariable("AuditAggregatorDb")
            ?? "Host=localhost;Port=5432;Database=audit_aggregator_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AuditAggregateDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuditAggregateDbContext(options);
    }
}
