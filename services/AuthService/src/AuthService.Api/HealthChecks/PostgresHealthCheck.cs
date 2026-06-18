using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthService.Api.HealthChecks;

/// <summary>
/// #AUTH-60: ready probe — verify PostgreSQL accept query. Dùng <c>SELECT 1</c> qua
/// <see cref="DatabaseFacade.CanConnectAsync"/> để tránh load EF metadata không cần thiết.
/// </summary>
public class PostgresHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;

    public PostgresHealthCheck(ApplicationDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ok = await _db.Database.CanConnectAsync(cancellationToken);
            return ok
                ? HealthCheckResult.Healthy("PostgreSQL reachable.")
                : HealthCheckResult.Unhealthy("CanConnectAsync returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL probe failed.", ex);
        }
    }
}
