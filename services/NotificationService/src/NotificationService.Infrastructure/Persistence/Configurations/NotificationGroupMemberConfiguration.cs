using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/// <summary>Sprint 6.4 NOTI4-01 — bảng nối nhiều-nhiều người ↔ nhóm.</summary>
public class NotificationGroupMemberConfiguration : IEntityTypeConfiguration<NotificationGroupMember>
{
    public void Configure(EntityTypeBuilder<NotificationGroupMember> builder)
    {
        builder.ToTable("notification_group_members");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.AddedBy).HasColumnName("added_by");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Một người chỉ vào một nhóm một lần. Partial vì soft delete: bỏ khỏi nhóm rồi thêm lại được.
        builder.HasIndex(x => new { x.GroupId, x.UserId })
               .IsUnique()
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ux_notification_group_members_pair");

        // Chiều ngược: "người này đang ở những nhóm nào".
        builder.HasIndex(x => x.UserId)
               .HasFilter("is_deleted = false")
               .HasDatabaseName("ix_notification_group_members_user");

        builder.Ignore(x => x.DomainEvents);
    }
}
