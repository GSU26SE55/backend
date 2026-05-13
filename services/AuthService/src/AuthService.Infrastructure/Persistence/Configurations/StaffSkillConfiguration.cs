using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class StaffSkillConfiguration : IEntityTypeConfiguration<StaffSkill>
{
    public void Configure(EntityTypeBuilder<StaffSkill> builder)
    {
        builder.ToTable("staff_skills");

        builder.HasKey(skill => skill.Id);

        builder.Property(skill => skill.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(skill => skill.StaffAccountId)
            .HasColumnName("staff_account_id")
            .IsRequired();

        builder.Property(skill => skill.SkillCode)
            .HasColumnName("skill_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(skill => skill.SkillLevel)
            .HasColumnName("skill_level")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(skill => skill.CertifiedUntil)
            .HasColumnName("certified_until")
            .HasColumnType("date");

        builder.Property(skill => skill.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(skill => skill.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(skill => skill.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(skill => skill.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(skill => skill.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(skill => new { skill.StaffAccountId, skill.SkillCode })
            .IsUnique();
        builder.HasIndex(skill => skill.SkillCode);
        builder.HasIndex(skill => skill.IsDeleted);

        builder.HasQueryFilter(skill => !skill.IsDeleted);

        builder.Ignore(skill => skill.DomainEvents);
    }
}
