using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class AccountRoleConfiguration : IEntityTypeConfiguration<AccountRole>
{
    public void Configure(EntityTypeBuilder<AccountRole> builder)
    {
        builder.ToTable("account_roles");

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ar => ar.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(ar => ar.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(ar => ar.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(ar => ar.AssignedBy)
            .HasColumnName("assigned_by");

        builder.Property(ar => ar.ExpiredAt)
            .HasColumnName("expired_at");

        builder.Property(ar => ar.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(ar => ar.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(ar => ar.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(ar => ar.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(ar => ar.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(ar => ar.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(ar => new { ar.AccountId, ar.RoleId })
            .IsUnique()
            .HasFilter("\"is_deleted\" = false");

        builder.HasIndex(ar => ar.AccountId);
        builder.HasIndex(ar => ar.RoleId);
        builder.HasIndex(ar => ar.IsActive);

        builder.HasQueryFilter(ar => !ar.IsDeleted);

        builder.HasOne(ar => ar.Account)
            .WithMany(a => a.AccountRoles)
            .HasForeignKey(ar => ar.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ar => ar.Role)
            .WithMany(r => r.AccountRoles)
            .HasForeignKey(ar => ar.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(ar => ar.DomainEvents);
    }
}
