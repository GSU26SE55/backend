using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Persistence.Configurations;

public class SmsGatewayDeviceConfiguration : IEntityTypeConfiguration<SmsGatewayDevice>
{
    public void Configure(EntityTypeBuilder<SmsGatewayDevice> b)
    {
        b.ToTable("sms_gateway_devices");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.DeviceName).HasColumnName("device_name").HasMaxLength(64).IsRequired();
        b.Property(x => x.DeviceCode).HasColumnName("device_code").HasMaxLength(64).IsRequired();
        b.Property(x => x.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(256).IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        b.Property(x => x.DailyLimit).HasColumnName("daily_limit").HasDefaultValue(100);
        b.Property(x => x.SentToday).HasColumnName("sent_today").HasDefaultValue(0);
        b.Property(x => x.SentTodayDate).HasColumnName("sent_today_date").HasColumnType("date");
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        b.Property(x => x.LastSeenIp).HasColumnName("last_seen_ip").HasMaxLength(64);

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(x => x.DeviceCode).IsUnique().HasDatabaseName("ux_sms_gateway_devices_device_code");

        b.Ignore(x => x.DomainEvents);
    }
}
