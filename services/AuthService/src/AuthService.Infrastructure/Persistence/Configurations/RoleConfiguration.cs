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

        builder.HasData(
            new Role
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Admin",
                NormalizedName = "ADMIN",
                Description = "Quản trị viên hệ thống, có toàn quyền.",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsDeleted = false
            },
            new Role
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Manager",
                NormalizedName = "MANAGER",
                Description = "Quản lý vận hành, điều phối kỹ thuật viên và đơn bảo trì.",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsDeleted = false
            },
            new Role
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Staff",
                NormalizedName = "STAFF",
                Description = "Nhân viên vận hành hệ thống.",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsDeleted = false
            },
            new Role
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Customer",
                NormalizedName = "CUSTOMER",
                Description = "Khách hàng sử dụng dịch vụ bảo trì pin năng lượng mặt trời.",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsDeleted = false
            }
        );
    }
}
