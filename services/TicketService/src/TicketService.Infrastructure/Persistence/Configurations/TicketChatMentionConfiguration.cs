using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatMentionConfiguration : IEntityTypeConfiguration<TicketChatMention>
{
    public void Configure(EntityTypeBuilder<TicketChatMention> builder)
    {
        builder.ToTable("ticket_chat_mentions");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id");

        builder.Property(e => e.MentionedUserId)
            .HasColumnName("mentioned_user_id");

        builder.Property(e => e.MentionedUserRole)
            .HasColumnName("mentioned_user_role")
            .HasConversion<int>();

        builder.Property(e => e.MentionedDisplayName)
            .HasColumnName("mentioned_display_name")
            .HasMaxLength(256);

        builder.Property(e => e.IsAcknowledged)
            .HasColumnName("is_acknowledged");

        builder.Property(e => e.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(e => e.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted");

        builder.Property(e => e.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(e => new { e.MentionedUserId, e.IsAcknowledged })
            .HasDatabaseName("ix_ticket_chat_mentions_user_unread");

        builder.HasIndex(e => e.ChatId)
            .HasDatabaseName("ix_ticket_chat_mentions_chat_id");

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
