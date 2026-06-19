using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Persistence;

/// <summary>
/// DbContext của SmsService — sở hữu 4 bảng (sms_messages, sms_gateway_devices, sms_audit_logs, outbox_messages).
/// Constructor không-tham-số dành cho design-time (<c>dotnet ef</c>). Constructor có
/// <see cref="AuditableEntityInterceptor"/> dành cho runtime — interceptor auto-set
/// CreatedAt/CreatedBy/UpdatedAt + convert Delete → soft-delete (IsDeleted=true).
/// </summary>
public class SmsDbContext : DbContext
{
    private readonly AuditableEntityInterceptor? _auditableEntityInterceptor;

    public SmsDbContext()
    {
    }

    /// <summary>
    /// Constructor runtime — DI inject <see cref="AuditableEntityInterceptor"/>.
    /// Interceptor auto-set CreatedAt/CreatedBy/UpdatedAt + convert Delete → soft-delete.
    /// </summary>
    public SmsDbContext(
        DbContextOptions<SmsDbContext> options,
        AuditableEntityInterceptor auditableEntityInterceptor) : base(options)
    {
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public virtual DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public virtual DbSet<SmsGatewayDevice> SmsGatewayDevices => Set<SmsGatewayDevice>();
    public virtual DbSet<SmsAuditLog> SmsAuditLogs => Set<SmsAuditLog>();
    public virtual DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_auditableEntityInterceptor is not null)
            optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmsDbContext).Assembly);

        if (Database.IsNpgsql())
        {
            // Postgres `xmin` system column dùng làm optimistic concurrency token —
            // 2 device đồng thời claim cùng row sẽ throw DbUpdateConcurrencyException.
            modelBuilder.Entity<SmsMessage>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // Đồng tương: IncrementSent / ResetDailyCounter cần race-free khi nhiều request
            // cùng device update SentToday đồng thời.
            modelBuilder.Entity<SmsGatewayDevice>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        }

        base.OnModelCreating(modelBuilder);
    }
}
