using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("staff_profiles");

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(profile => profile.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(profile => profile.EmployeeCode)
            .HasColumnName("employee_code")
            .HasMaxLength(50);

        builder.Property(profile => profile.Department)
            .HasColumnName("department")
            .HasMaxLength(100);

        builder.Property(profile => profile.MaxConcurrentTickets)
            .HasColumnName("max_concurrent_tickets")
            .HasDefaultValue(3)
            .IsRequired();

        builder.Property(profile => profile.IsAvailable)
            .HasColumnName("is_available")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(profile => profile.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(profile => profile.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(profile => profile.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(profile => profile.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(profile => profile.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(profile => profile.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(profile => profile.AccountId).IsUnique();
        builder.HasIndex(profile => profile.EmployeeCode)
            .IsUnique()
            .HasFilter("\"employee_code\" IS NOT NULL");
        builder.HasIndex(profile => profile.IsAvailable);
        builder.HasIndex(profile => profile.IsDeleted);

        builder.HasOne(profile => profile.Account)
            .WithOne(account => account.StaffProfile)
            .HasForeignKey<StaffProfile>(profile => profile.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(profile => profile.Skills)
            .WithOne(skill => skill.StaffProfile)
            .HasForeignKey(skill => skill.StaffAccountId)
            .HasPrincipalKey(profile => profile.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(profile => !profile.IsDeleted);

        builder.Ignore(profile => profile.DomainEvents);
    }
}
