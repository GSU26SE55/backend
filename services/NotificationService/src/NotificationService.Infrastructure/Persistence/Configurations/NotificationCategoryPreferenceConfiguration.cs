using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.3 NOTI3-04 (#704).</summary>
public class NotificationCategoryPreferenceConfiguration : IEntityTypeConfiguration<NotificationCategoryPreference>
{
    public void Configure(EntityTypeBuilder<NotificationCategoryPreference> builder)
    {
        builder.ToTable("notification_category_preferences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<int>().IsRequired();
        builder.Property(x => x.PushEnabled).HasColumnName("push_enabled").HasDefaultValue(true);
        builder.Property(x => x.EmailEnabled).HasColumnName("email_enabled").HasDefaultValue(true);
        builder.Property(x => x.SmsEnabled).HasColumnName("sms_enabled").HasDefaultValue(false);
        builder.Property(x => x.InAppEnabled).HasColumnName("in_app_enabled").HasDefaultValue(true);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Một user chỉ có đúng một dòng cho mỗi nhóm.
        builder.HasIndex(x => new { x.UserId, x.Category })
               .IsUnique()
               .HasDatabaseName("ux_notification_category_preferences_user_category");

        builder.Ignore(x => x.DomainEvents);
    }
}
