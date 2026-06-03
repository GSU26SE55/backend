using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketActivityConfiguration : IEntityTypeConfiguration<TicketActivity>
{
    public void Configure(EntityTypeBuilder<TicketActivity> builder)
    {
        builder.ToTable("ticket_activities");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(e => e.ActorRole)
            .HasColumnName("actor_role")
            .HasConversion<int>();

        builder.Property(e => e.ActorDisplayName)
            .HasColumnName("actor_display_name")
            .HasMaxLength(256);

        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasConversion<int>();

        builder.Property(e => e.OldValue)
            .HasColumnName("old_value");

        builder.Property(e => e.NewValue)
            .HasColumnName("new_value");

        builder.Property(e => e.Reason)
            .HasColumnName("reason");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.ActorUserId);

        builder.HasOne(e => e.Ticket)
            .WithMany(e => e.Activities)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
