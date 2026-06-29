using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Infrastructure.Sagas.ChatEscalationReview;

namespace TicketService.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping cho <see cref="ChatEscalationReviewSagaState"/>.
/// CorrelationId = ChatId. Optimistic concurrency qua PostgreSQL xmin. #566.
/// </summary>
public class ChatEscalationReviewSagaStateConfiguration : IEntityTypeConfiguration<ChatEscalationReviewSagaState>
{
    public void Configure(EntityTypeBuilder<ChatEscalationReviewSagaState> builder)
    {
        builder.ToTable("chat_escalation_review_saga_states");

        builder.HasKey(s => s.CorrelationId);

        builder.Property(s => s.CorrelationId)
            .HasColumnName("correlation_id")
            .ValueGeneratedNever();

        builder.Property(s => s.CurrentState)
            .HasColumnName("current_state")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.Version)
            .HasColumnName("version");

        builder.Property(s => s.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(s => s.TicketCode).HasColumnName("ticket_code").HasMaxLength(50).IsRequired();
        builder.Property(s => s.ManagerUserId).HasColumnName("manager_user_id").IsRequired();
        builder.Property(s => s.EscalationReason).HasColumnName("escalation_reason").HasMaxLength(500).IsRequired();

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(s => s.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(s => s.PendingTimeoutTokenId).HasColumnName("pending_timeout_token_id");

        builder.HasIndex(s => s.CurrentState)
            .HasDatabaseName("ix_chat_escalation_review_saga_states_current_state");
        builder.HasIndex(s => s.TicketId)
            .HasDatabaseName("ix_chat_escalation_review_saga_states_ticket_id");
        builder.HasIndex(s => s.StartedAt)
            .HasDatabaseName("ix_chat_escalation_review_saga_states_started_at");
    }
}
