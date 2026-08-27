using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class StaffAccountConfiguration : IEntityTypeConfiguration<StaffAccount>
{
    public void Configure(EntityTypeBuilder<StaffAccount> builder)
    {
        builder.ToTable("staff_accounts");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.AccountId)
            .HasColumnName("account_id");

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(256);

        builder.Property(e => e.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(256);

        builder.Property(e => e.EmployeeCode)
            .HasColumnName("employee_code")
            .HasMaxLength(50);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(e => e.IsAvailable)
            .HasColumnName("is_available");

        builder.Property(e => e.MaxConcurrentTickets)
            .HasColumnName("max_concurrent_tickets");

        builder.Property(e => e.SkillCodes)
            .HasColumnName("skill_codes")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        // Mặc định "Staff" cho hàng cũ — bảng này chứa cả Manager/Admin, xem StaffAccount.Role.
        builder.Property(e => e.Role)
            .HasColumnName("role")
            .HasMaxLength(20)
            .HasDefaultValue("Staff")
            .IsRequired();

        builder.Property(e => e.LastSyncedAt)
            .HasColumnName("last_synced_at");

        builder.Property(e => e.LastSourceEventAtUtc)
            .HasColumnName("last_source_event_at_utc");

        builder.Property(e => e.LastStaffProfileSourceEventAtUtc)
            .HasColumnName("last_staff_profile_source_event_at_utc");

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

        builder.HasIndex(e => e.AccountId);
        builder.HasIndex(e => e.Email);
        builder.HasIndex(e => e.EmployeeCode).IsUnique();
        builder.HasIndex(e => e.Status);
    }
}
