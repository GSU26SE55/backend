using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketAssignmentConfiguration : IEntityTypeConfiguration<TicketAssignment>
{
    public void Configure(EntityTypeBuilder<TicketAssignment> builder)
    {
        builder.ToTable("ticket_assignments");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.StaffId)
            .HasColumnName("staff_id");

        builder.Property(e => e.Role)
            .HasColumnName("role")
            .HasConversion<int>();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(e => e.Ticket)
            .WithMany(t => t.Assignments)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // One staff can have at most one active assignment per ticket
        builder.HasIndex(e => new { e.TicketId, e.StaffId })
            .HasDatabaseName("ix_ticket_assignments_ticket_staff")
            .IsUnique();

        builder.HasIndex(e => new { e.TicketId, e.Role })
            .HasDatabaseName("ix_ticket_assignments_ticket_role");
    }
}
