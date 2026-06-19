using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;

namespace AuthService.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    private readonly AuditableEntityInterceptor? _auditableEntityInterceptor;

    public ApplicationDbContext()
    {
    }
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options,
        AuditableEntityInterceptor auditableEntityInterceptor) : base(options)
    {
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public virtual DbSet<Account> Users { get; set; }
    public virtual DbSet<Role> Roles { get; set; }
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<AuditLog> AuditLogs { get; set; }
    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; }
    public virtual DbSet<Permission> Permissions { get; set; }
    public virtual DbSet<RolePermission> RolePermissions { get; set; }
    public virtual DbSet<AccountProfile> AccountProfiles { get; set; }
    public virtual DbSet<StaffProfile> StaffProfiles { get; set; }
    public virtual DbSet<StaffSkill> StaffSkills { get; set; }
    public virtual DbSet<BackupCode> BackupCodes { get; set; }
    public virtual DbSet<TrustedDevice> TrustedDevices { get; set; }
    public virtual DbSet<AccountMergeLog> AccountMergeLogs { get; set; }
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
