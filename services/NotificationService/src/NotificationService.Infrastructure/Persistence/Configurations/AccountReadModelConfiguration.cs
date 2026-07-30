using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class AccountReadModelConfiguration : IEntityTypeConfiguration<AccountReadModel>
{
    public void Configure(EntityTypeBuilder<AccountReadModel> builder)
    {
        builder.ToTable("account_read_models");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(32);
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(64).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.PreferredLocale).HasColumnName("preferred_locale").HasMaxLength(16); // NOTI3-12 (#712)
        builder.Property(x => x.LastSyncedAtUtc).HasColumnName("last_synced_at_utc").IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Index phục vụ resolve recipient theo role (broadcast Manager/Admin).
        builder.HasIndex(x => new { x.Role, x.IsActive });

        builder.Ignore(x => x.DomainEvents);
    }
}
