using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketChatEditConfiguration : IEntityTypeConfiguration<TicketChatEdit>
{
    public void Configure(EntityTypeBuilder<TicketChatEdit> builder)
    {
        builder.ToTable("ticket_chat_edits");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id");

        builder.Property(e => e.OldBody)
            .HasColumnName("old_body")
            .IsRequired();

        builder.Property(e => e.NewBody)
            .HasColumnName("new_body")
            .IsRequired();

        builder.Property(e => e.EditedAt)
            .HasColumnName("edited_at");

        builder.Property(e => e.EditedByUserId)
            .HasColumnName("edited_by_user_id");

        builder.Property(e => e.EditedByRole)
            .HasColumnName("edited_by_role")
            .HasConversion<int>();

        builder.Property(e => e.EditReason)
            .HasColumnName("edit_reason");

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

        builder.HasIndex(e => e.ChatId);

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
