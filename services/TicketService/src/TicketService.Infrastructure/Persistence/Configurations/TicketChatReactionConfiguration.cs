using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatReactionConfiguration : IEntityTypeConfiguration<TicketChatReaction>
{
    public void Configure(EntityTypeBuilder<TicketChatReaction> builder)
    {
        builder.ToTable("ticket_chat_reactions");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id");

        builder.Property(e => e.UserRole)
            .HasColumnName("user_role")
            .HasConversion<int>();

        builder.Property(e => e.ReactionType)
            .HasColumnName("reaction_type")
            .HasConversion<int>();

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

        builder.HasIndex(e => new { e.ChatId, e.UserId, e.ReactionType })
            .IsUnique()
            .HasDatabaseName("ix_ticket_chat_reactions_chat_user_type");

        builder.HasIndex(e => new { e.ChatId, e.ReactionType })
            .HasDatabaseName("ix_ticket_chat_reactions_chat_type");

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
