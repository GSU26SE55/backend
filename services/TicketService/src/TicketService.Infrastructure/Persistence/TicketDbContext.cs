using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Sagas;
using TicketService.Infrastructure.Sagas.ChatEscalationReview;

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
    public virtual DbSet<TicketBatteryAsset> TicketBatteryAssets { get; set; }
    public virtual DbSet<TicketActivity> TicketActivities { get; set; }
    public virtual DbSet<TicketChat> TicketChats { get; set; }
    public virtual DbSet<TicketChatEdit> TicketChatEdits { get; set; }
    public virtual DbSet<TicketAttachment> TicketAttachments { get; set; }
    public virtual DbSet<SlaTimer> SlaTimers { get; set; }
    public virtual DbSet<SlaPauseEvent> SlaPauseEvents { get; set; }
    public virtual DbSet<MaintenanceLog> MaintenanceLogs { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<TicketAuditLog> TicketAuditLogs { get; set; }       // Sprint audit #AUDIT-24
    public virtual DbSet<TicketAuditOutbox> TicketAuditOutboxes { get; set; } // Sprint audit #AUDIT-25
    public virtual DbSet<CustomerAccount> CustomerAccounts { get; set; }
    public virtual DbSet<StaffAccount> StaffAccounts { get; set; }
    public virtual DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; }
    public virtual DbSet<KbArticleVersion> KbArticleVersions { get; set; }
    public virtual DbSet<TicketKbReference> TicketKbReferences { get; set; }
    public virtual DbSet<BlogPost> BlogPosts { get; set; }
    public virtual DbSet<BlogPostVersion> BlogPostVersions { get; set; }
    public virtual DbSet<BlogTemplate> BlogTemplates { get; set; }
    public virtual DbSet<AlertTicketSagaState> AlertTicketSagaStates { get; set; }
    public virtual DbSet<TicketParticipant> TicketParticipants { get; set; }
    public virtual DbSet<TicketChatMention> TicketChatMentions { get; set; }
    public virtual DbSet<TicketChatReaction> TicketChatReactions { get; set; }
    public virtual DbSet<TicketChatRead> TicketChatReads { get; set; }
    public virtual DbSet<TicketChatHide> TicketChatHides { get; set; }
    public virtual DbSet<ChatTemplate> ChatTemplates { get; set; }
    public virtual DbSet<ChatAiSuggestion> ChatAiSuggestions { get; set; }
    public virtual DbSet<TicketChatTranslation> TicketChatTranslations { get; set; }
    public virtual DbSet<TicketChatTranslationUser> TicketChatTranslationUsers { get; set; }
    public virtual DbSet<ChatMetricsDaily> ChatMetricsDailies { get; set; }
    public virtual DbSet<ChatEscalationReviewSagaState> ChatEscalationReviewSagaStates { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_auditableEntityInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketDbContext).Assembly);

        if (Database.IsNpgsql())
        {
            // PostgreSQL xmin optimistic concurrency token (per overall.md §53.8).
            modelBuilder.Entity<AlertTicketSagaState>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            modelBuilder.Entity<ChatEscalationReviewSagaState>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        }

        base.OnModelCreating(modelBuilder);
    }
}
