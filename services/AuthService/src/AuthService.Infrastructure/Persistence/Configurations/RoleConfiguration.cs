using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(RoleStatusEnum.Active)
            .IsRequired();

        builder.Property(r => r.IsSystemRole)
            .HasColumnName("is_system_role")
            .HasDefaultValue(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(r => r.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(r => r.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(r => r.NormalizedName).IsUnique();
        builder.HasIndex(r => r.Status);

        builder.HasQueryFilter(r => !r.IsDeleted);

        // Account ↔ Role mapped trên AccountConfiguration (1 Role → nhiều Account).

        builder.Ignore(r => r.DomainEvents);

        // System roles (Admin/Manager/Staff/Customer) được seed runtime trong AuthDataSeeder
        // với Guid.NewGuid() — không hardcode qua HasData để tránh Id cố định trong migration.
    }
}
