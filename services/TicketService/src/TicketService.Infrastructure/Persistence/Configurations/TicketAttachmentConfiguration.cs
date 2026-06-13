using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.UploadedByUserId)
            .HasColumnName("uploaded_by_user_id");

        builder.Property(e => e.FileId)
            .HasColumnName("file_id");

        builder.Property(e => e.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(256);

        builder.Property(e => e.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100);

        builder.Property(e => e.SizeBytes)
            .HasColumnName("size_bytes");

        builder.Property(e => e.PublicUrl)
            .HasColumnName("public_url");

        builder.Property(e => e.Source)
            .HasColumnName("source")
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

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.FileId);

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.Attachments)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
