using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Persistence.Configurations;

public class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> b)
    {
        b.ToTable("sms_messages");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20).IsRequired();
        b.Property(x => x.Message).HasColumnName("message").HasMaxLength(1600);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(x => x.MaxRetryCount).HasColumnName("max_retry_count").HasDefaultValue(3);
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);

        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(32);
        b.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(32).IsRequired();
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
        b.Property(x => x.TargetDeviceCode).HasColumnName("target_device_code").HasMaxLength(64);

        b.Property(x => x.GatewayDeviceCode).HasColumnName("gateway_device_code").HasMaxLength(64);
        b.Property(x => x.GatewayDeviceId).HasColumnName("gateway_device_id");

        b.Property(x => x.PickedAt).HasColumnName("picked_at");
        b.Property(x => x.SentAt).HasColumnName("sent_at");
        b.Property(x => x.FailedAt).HasColumnName("failed_at");
        b.Property(x => x.RedactedAt).HasColumnName("redacted_at");

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_sms_messages_status_created_at");
        b.HasIndex(x => x.PhoneNumber).HasDatabaseName("ix_sms_messages_phone_number");
        b.HasIndex(x => x.CorrelationId).HasDatabaseName("ix_sms_messages_correlation_id");
        b.HasIndex(x => new { x.Status, x.SentAt })
            .HasDatabaseName("ix_sms_messages_status_sent_at");
        b.HasIndex(x => new { x.TargetDeviceCode, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_sms_messages_target_status_created_at");

        b.Ignore(x => x.DomainEvents);
    }
}
