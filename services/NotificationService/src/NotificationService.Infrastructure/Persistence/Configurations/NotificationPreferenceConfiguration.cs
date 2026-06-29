using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(x => x.PushEnabled).HasColumnName("push_enabled").HasDefaultValue(true);
        builder.Property(x => x.EmailEnabled).HasColumnName("email_enabled").HasDefaultValue(true);
        builder.Property(x => x.SmsEnabled).HasColumnName("sms_enabled").HasDefaultValue(false);
        builder.Property(x => x.InAppEnabled).HasColumnName("in_app_enabled").HasDefaultValue(true);

        builder.Property(x => x.QuietHoursStart).HasColumnName("quiet_hours_start");
        builder.Property(x => x.QuietHoursEnd).HasColumnName("quiet_hours_end");
        builder.Property(x => x.TimeZone).HasColumnName("time_zone").HasMaxLength(64).HasDefaultValue("Asia/Ho_Chi_Minh");

        builder.Property(x => x.Frequency)
            .HasColumnName("frequency").HasConversion<int>()
            .HasDefaultValue(NotificationFrequencyEnum.Immediate);

        // Chat-specific preferences (#570)
        builder.Property(x => x.NotifyOnChat).HasColumnName("notify_on_chat").HasDefaultValue(true);
        builder.Property(x => x.NotifyOnMention).HasColumnName("notify_on_mention").HasDefaultValue(true);
        builder.Property(x => x.NotifyOnReaction).HasColumnName("notify_on_reaction").HasDefaultValue(false);
        builder.Property(x => x.DigestWindowMinutes).HasColumnName("digest_window_minutes");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(x => x.UserId).IsUnique();

        builder.Ignore(x => x.DomainEvents);
    }
}
