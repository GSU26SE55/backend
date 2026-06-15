using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Persistence.Configurations;

public class SmsAuditLogConfiguration : IEntityTypeConfiguration<SmsAuditLog>
{
    public void Configure(EntityTypeBuilder<SmsAuditLog> b)
    {
        b.ToTable("sms_audit_logs");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.SmsMessageId).HasColumnName("sms_message_id").IsRequired();
        b.Property(x => x.Event).HasColumnName("event").HasConversion<int>().IsRequired();
        b.Property(x => x.DeviceCode).HasColumnName("device_code").HasMaxLength(64);
        b.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1000);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => x.SmsMessageId).HasDatabaseName("ix_sms_audit_logs_sms_message_id");
        b.HasIndex(x => new { x.SmsMessageId, x.CreatedAt })
            .HasDatabaseName("ix_sms_audit_logs_sms_message_id_created_at");

        b.Ignore(x => x.DomainEvents);
    }
}
