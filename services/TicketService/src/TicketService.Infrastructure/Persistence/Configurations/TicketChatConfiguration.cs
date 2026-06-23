using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatConfiguration : IEntityTypeConfiguration<TicketChat>
{
    public void Configure(EntityTypeBuilder<TicketChat> builder)
    {
        builder.ToTable("ticket_chats");

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
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<Guid>>());

        builder.Property(e => e.EditedAt)
            .HasColumnName("edited_at");

        builder.Property(e => e.EditCount)
            .HasColumnName("edit_count")
            .HasDefaultValue(0);

        builder.Property(e => e.LastEditedByUserId)
            .HasColumnName("last_edited_by_user_id");

        builder.Property(e => e.BodyFormat)
            .HasColumnName("body_format")
            .HasConversion<int>()
            .HasDefaultValue(ChatBodyFormatEnum.PlainText);

        builder.Property(e => e.BodyHtml)
            .HasColumnName("body_html");

        builder.Property(e => e.ParentChatId)
            .HasColumnName("parent_chat_id");

        builder.Property(e => e.ThreadRootId)
            .HasColumnName("thread_root_id");

        builder.Property(e => e.ReplyCount)
            .HasColumnName("reply_count")
            .HasDefaultValue(0);

        builder.Property(e => e.IsPinned)
            .HasColumnName("is_pinned")
            .HasDefaultValue(false);

        builder.Property(e => e.PinnedAt)
            .HasColumnName("pinned_at");

        builder.Property(e => e.PinnedByUserId)
            .HasColumnName("pinned_by_user_id");

        builder.Property(e => e.OriginalLanguage)
            .HasColumnName("original_language")
            .HasMaxLength(5);

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
        builder.HasIndex(e => e.ParentChatId)
            .HasDatabaseName("ix_ticket_chats_parent");
        builder.HasIndex(e => e.ThreadRootId)
            .HasDatabaseName("ix_ticket_chats_thread_root");

        builder.HasIndex(e => new { e.TicketId, e.CreatedAt })
            .HasDatabaseName("ix_ticket_chats_ticket_created_at")
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.TicketId, e.IsPinned, e.CreatedAt })
            .HasDatabaseName("ix_ticket_chats_ticket_pinned_created_at")
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.AuthorUserId, e.CreatedAt })
            .HasDatabaseName("ix_ticket_chats_author_created_at")
            .IsDescending(false, true);

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.Chats)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ParentChat)
            .WithMany()
            .HasForeignKey(e => e.ParentChatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
