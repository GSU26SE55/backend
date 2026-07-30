using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(16).IsRequired();
        builder.Property(x => x.TitleTemplate).HasColumnName("title_template").HasMaxLength(500).IsRequired();
        builder.Property(x => x.BodyTemplate).HasColumnName("body_template").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1);   // NOTI3-12 (#712)
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Sprint 6.3 NOTI3-12 (#712) — unique nay gồm cả Version: nhiều bản cùng tồn tại để rollback.
        // Ràng buộc "chỉ 1 bản IsActive mỗi bộ ba" là unique CÓ ĐIỀU KIỆN, không thể diễn đạt bằng
        // HasIndex thường nên khai bằng filter (Postgres partial unique index).
        builder.HasIndex(x => new { x.Type, x.Channel, x.Locale, x.Version })
               .IsUnique()
               .HasDatabaseName("ux_notification_templates_type_channel_locale_version");

        builder.HasIndex(x => new { x.Type, x.Channel, x.Locale })
               .IsUnique()
               .HasFilter("is_active = true AND is_deleted = false")
               .HasDatabaseName("ux_notification_templates_active_per_key");

        builder.Ignore(x => x.DomainEvents);
    }
}
