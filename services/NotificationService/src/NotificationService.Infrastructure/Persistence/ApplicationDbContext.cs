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
    public virtual DbSet<PushReceipt> PushReceipts { get; set; }                     // Sprint 6.3 NOTI3-02 (#702)
    public virtual DbSet<NotificationCategoryPreference> NotificationCategoryPreferences { get; set; } // Sprint 6.3 NOTI3-04 (#704)
    public virtual DbSet<NotificationGroup> NotificationGroups { get; set; }             // Sprint 6.4 NOTI4-01
    public virtual DbSet<NotificationGroupMember> NotificationGroupMembers { get; set; } // Sprint 6.4 NOTI4-01
    public virtual DbSet<NotificationBatch> NotificationBatches { get; set; }             // Sprint 6.4 NOTI4-06
    public virtual DbSet<NotificationBatchTarget> NotificationBatchTargets { get; set; }  // Sprint 6.4 NOTI4-06
    public virtual DbSet<NotificationSetting> NotificationSettings { get; set; }          // ADR-0019 — push transport

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
