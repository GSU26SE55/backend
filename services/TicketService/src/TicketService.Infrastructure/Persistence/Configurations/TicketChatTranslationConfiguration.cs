using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatTranslationConfiguration : IEntityTypeConfiguration<TicketChatTranslation>
{
    public void Configure(EntityTypeBuilder<TicketChatTranslation> builder)
    {
        builder.ToTable("ticket_chat_translations");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id");

        builder.Property(e => e.TargetLanguage)
            .HasColumnName("target_language")
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(e => e.TranslatedBody)
            .HasColumnName("translated_body")
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasConversion<int>();

        builder.Property(e => e.TranslatedAt)
            .HasColumnName("translated_at");

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

        builder.HasIndex(e => new { e.ChatId, e.TargetLanguage })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_ticket_chat_translations_chat_lang");

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
