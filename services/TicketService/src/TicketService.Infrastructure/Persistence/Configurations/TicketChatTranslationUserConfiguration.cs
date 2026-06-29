using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatTranslationUserConfiguration : IEntityTypeConfiguration<TicketChatTranslationUser>
{
    public void Configure(EntityTypeBuilder<TicketChatTranslationUser> builder)
    {
        builder.ToTable("ticket_chat_translation_users");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TranslationId)
            .HasColumnName("translation_id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id");

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

        builder.HasIndex(e => new { e.TranslationId, e.UserId })
            .IsUnique()
            .HasDatabaseName("ix_chat_translation_users_uniq");

        builder.HasOne(e => e.Translation)
            .WithMany(t => t.Users)
            .HasForeignKey(e => e.TranslationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
