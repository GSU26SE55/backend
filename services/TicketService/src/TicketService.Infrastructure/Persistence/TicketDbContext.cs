using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Sagas;

namespace TicketService.Infrastructure.Persistence;

public class TicketDbContext : DbContext
{
    private readonly AuditableEntityInterceptor _auditableEntityInterceptor;

    public TicketDbContext(DbContextOptions<TicketDbContext> options,
        AuditableEntityInterceptor auditableEntityInterceptor) : base(options)
    {
        _auditableEntityInterceptor = auditableEntityInterceptor;
    }

    public virtual DbSet<Ticket> Tickets { get; set; }
    public virtual DbSet<TicketActivity> TicketActivities { get; set; }
    public virtual DbSet<TicketComment> TicketComments { get; set; }
    public virtual DbSet<TicketAttachment> TicketAttachments { get; set; }
    public virtual DbSet<SlaTimer> SlaTimers { get; set; }
    public virtual DbSet<SlaPauseEvent> SlaPauseEvents { get; set; }
    public virtual DbSet<MaintenanceLog> MaintenanceLogs { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<CustomerAccount> CustomerAccounts { get; set; }
    public virtual DbSet<StaffAccount> StaffAccounts { get; set; }
    public virtual DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; }
    public virtual DbSet<TicketKbReference> TicketKbReferences { get; set; }
    public virtual DbSet<AlertTicketSagaState> AlertTicketSagaStates { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
