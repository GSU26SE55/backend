using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

public class NotificationSettingConfiguration : IEntityTypeConfiguration<NotificationSetting>
{
    public void Configure(EntityTypeBuilder<NotificationSetting> builder)
    {
        builder.ToTable("notification_settings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        // Một khoá đúng một dòng SỐNG. Unique để hai request PUT đồng thời không đẻ ra hai dòng
        // cùng khoá rồi mỗi lần đọc lại ra một giá trị khác nhau.
        //
        // Lọc theo is_deleted vì bảng dùng xoá mềm: nếu tính cả dòng đã xoá thì một lần xoá mềm sẽ
        // khoá vĩnh viễn khoá đó, không bao giờ tạo lại được.
        builder.HasIndex(x => x.Key).IsUnique().HasFilter("is_deleted = false");

        builder.Ignore(x => x.DomainEvents);
    }
}
