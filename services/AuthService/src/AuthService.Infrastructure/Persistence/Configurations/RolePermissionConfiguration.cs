using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(rp => rp.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(rp => rp.PermissionId)
            .HasColumnName("permission_id")
            .IsRequired();

        builder.Property(rp => rp.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(rp => rp.AssignedBy)
            .HasColumnName("assigned_by");

        builder.Property(rp => rp.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(rp => rp.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(rp => rp.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(rp => rp.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(rp => rp.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId })
            .IsUnique()
            .HasFilter("\"is_deleted\" = false")
            .HasDatabaseName("ux_role_permissions_role_permission_active");

        builder.HasIndex(rp => rp.RoleId)
            .HasDatabaseName("ix_role_permissions_role_id");

        builder.HasIndex(rp => rp.PermissionId)
            .HasDatabaseName("ix_role_permissions_permission_id");

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(rp => !rp.IsDeleted);

        builder.Ignore(rp => rp.DomainEvents);
    }
}
