using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Entities;
using SharedInfrastructure.Persistence.Interceptors;

namespace NotificationService.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    private readonly AuditableEntityInterceptor? _auditableEntityInterceptor;

    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        AuditableEntityInterceptor auditableEntityInterceptor) : base(options)
    {
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public virtual DbSet<Notification> Notifications { get; set; }
    public virtual DbSet<DeviceToken> DeviceTokens { get; set; }
    public virtual DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public virtual DbSet<NotificationTemplate> NotificationTemplates { get; set; }
    public virtual DbSet<AccountReadModel> AccountReadModels { get; set; }
    public virtual DbSet<NotificationAuditLog> NotificationAuditLogs { get; set; }       // Sprint audit #AUDIT-34
    public virtual DbSet<NotificationAuditOutbox> NotificationAuditOutboxes { get; set; } // Sprint audit #AUDIT-34

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_auditableEntityInterceptor is not null)
            optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
