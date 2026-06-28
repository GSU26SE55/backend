using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class ChatTemplateConfiguration : IEntityTypeConfiguration<ChatTemplate>
{
    public void Configure(EntityTypeBuilder<ChatTemplate> builder)
    {
        builder.ToTable("chat_templates");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasConversion<int>();

        builder.Property(e => e.IsInternalDefault)
            .HasColumnName("is_internal_default")
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(e => e.Scope)
            .HasColumnName("scope")
            .HasConversion<int>();

        builder.Property(e => e.UsageCount)
            .HasColumnName("usage_count")
            .HasDefaultValue(0);

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

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

        builder.HasIndex(e => e.Scope)
            .HasDatabaseName("ix_chat_templates_scope");
    }
}
