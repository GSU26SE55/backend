using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

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

        builder.Property(e => e.Source)
            .HasColumnName("source")
            .HasConversion<int>();

        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id");

        builder.Property(e => e.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(1000);

        builder.Property(e => e.Url)
            .HasColumnName("url")
            .HasMaxLength(2000);

        builder.Property(e => e.IsInline)
            .HasColumnName("is_inline")
            .HasDefaultValue(false);

        builder.Property(e => e.DownloadCount)
            .HasColumnName("download_count")
            .HasDefaultValue(0);

        builder.Property(e => e.VirusScanStatus)
            .HasColumnName("virus_scan_status")
            .HasConversion<int>()
            .HasDefaultValue(VirusScanStatusEnum.Pending);

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
        builder.HasIndex(e => e.ChatId);

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.Attachments)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
