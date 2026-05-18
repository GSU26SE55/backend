using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.AuthorUserId)
            .HasColumnName("author_user_id");

        builder.Property(e => e.AuthorRole)
            .HasColumnName("author_role")
            .HasConversion<int>();

        builder.Property(e => e.AuthorDisplayName)
            .HasColumnName("author_display_name")
            .HasMaxLength(256);

        builder.Property(e => e.Body)
            .HasColumnName("body")
            .IsRequired();

        builder.Property(e => e.IsInternal)
            .HasColumnName("is_internal");

        builder.Property(e => e.AttachmentFileIds)
            .HasColumnName("attachment_file_ids")
            .HasColumnType("jsonb");

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

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.AuthorUserId);

        builder.HasOne(e => e.Ticket)
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
