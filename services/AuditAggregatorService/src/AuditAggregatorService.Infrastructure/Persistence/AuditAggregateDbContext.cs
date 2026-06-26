using AuditAggregatorService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuditAggregatorService.Infrastructure.Persistence;

/// <summary>
/// DbContext read-store của AuditAggregatorService (Sprint audit <c>#AUDIT-14</c>).
/// Map <see cref="AuditAggregate"/> → bảng <c>audit_aggregate</c> (partitioned by month, pg_partman).
/// </summary>
public class AuditAggregateDbContext : DbContext
{
    public AuditAggregateDbContext(DbContextOptions<AuditAggregateDbContext> options) : base(options) { }

    public DbSet<AuditAggregate> AuditAggregates => Set<AuditAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditAggregateDbContext).Assembly);
    }
}
