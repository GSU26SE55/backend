using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketKbReferenceConfiguration : IEntityTypeConfiguration<TicketKbReference>
{
    public void Configure(EntityTypeBuilder<TicketKbReference> builder)
    {
        builder.ToTable("ticket_kb_references");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.KbArticleId)
            .HasColumnName("kb_article_id");

        builder.Property(e => e.KbArticleCode)
            .HasColumnName("kb_article_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ReferencedByUserId)
            .HasColumnName("referenced_by_user_id");

        builder.Property(e => e.ReferenceType)
            .HasColumnName("reference_type")
            .HasConversion<int>();

        builder.Property(e => e.Note)
            .HasColumnName("note");

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

        builder.HasIndex(e => new { e.TicketId, e.KbArticleId, e.ReferenceType }).IsUnique();

        builder.HasOne<Ticket>()
            .WithMany(e => e.KbReferences)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
