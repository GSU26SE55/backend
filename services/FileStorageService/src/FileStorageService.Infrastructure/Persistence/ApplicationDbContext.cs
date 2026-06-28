using FileStorageService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;

namespace FileStorageService.Infrastructure.Persistence;

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

    public virtual DbSet<UploadedFile> UploadedFiles { get; set; }
    public virtual DbSet<FileAuditLog> FileAuditLogs { get; set; }       // Sprint audit #AUDIT-29
    public virtual DbSet<FileAuditOutbox> FileAuditOutboxes { get; set; } // Sprint audit #AUDIT-29

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
