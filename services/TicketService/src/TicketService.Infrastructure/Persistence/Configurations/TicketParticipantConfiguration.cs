using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketParticipantConfiguration : IEntityTypeConfiguration<TicketParticipant>
{
    public void Configure(EntityTypeBuilder<TicketParticipant> builder)
    {
        builder.ToTable("ticket_participants");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id");

        builder.Property(e => e.UserRole)
            .HasColumnName("user_role")
            .HasConversion<int>();

        builder.Property(e => e.ParticipantType)
            .HasColumnName("participant_type")
            .HasConversion<int>();

        builder.Property(e => e.CanPost)
            .HasColumnName("can_post");

        builder.Property(e => e.CanViewInternal)
            .HasColumnName("can_view_internal");

        builder.Property(e => e.AddedByUserId)
            .HasColumnName("added_by_user_id");

        builder.Property(e => e.AddedAt)
            .HasColumnName("added_at");

        builder.Property(e => e.RemovedAt)
            .HasColumnName("removed_at");

        builder.Property(e => e.RemovedByUserId)
            .HasColumnName("removed_by_user_id");

        builder.Property(e => e.RemoveReason)
            .HasColumnName("remove_reason");

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

        builder.HasIndex(e => e.TicketId)
            .HasDatabaseName("ix_ticket_participants_ticket_active")
            .HasFilter("removed_at IS NULL");

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.Participants)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
