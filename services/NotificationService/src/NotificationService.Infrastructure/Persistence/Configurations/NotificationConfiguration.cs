using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(n => n.Type)
            .HasColumnName("type").HasConversion<int>().IsRequired();

        builder.Property(n => n.Channel)
            .HasColumnName("channel").HasConversion<int>().IsRequired();

        builder.Property(n => n.Status)
            .HasColumnName("status").HasConversion<int>()
            .HasDefaultValue(NotificationStatusEnum.Pending).IsRequired();

        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(n => n.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(n => n.EntityType).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(n => n.EntityId).HasColumnName("entity_id");
        builder.Property(n => n.SentAt).HasColumnName("sent_at");
        builder.Property(n => n.ReadAt).HasColumnName("read_at");
        builder.Property(n => n.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);

        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.CreatedBy).HasColumnName("created_by");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");
        builder.Property(n => n.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(n => n.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(n => new { n.UserId, n.Status });
        builder.HasIndex(n => n.CreatedAt);
        builder.HasIndex(n => new { n.EntityType, n.EntityId });

        builder.Ignore(n => n.DomainEvents);
    }
}
