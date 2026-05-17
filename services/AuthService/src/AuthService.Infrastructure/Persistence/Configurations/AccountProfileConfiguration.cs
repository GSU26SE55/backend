using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class AccountProfileConfiguration : IEntityTypeConfiguration<AccountProfile>
{
    public void Configure(EntityTypeBuilder<AccountProfile> builder)
    {
        builder.ToTable("account_profiles");

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(profile => profile.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(profile => profile.AvatarFileId)
            .HasColumnName("avatar_file_id");

        builder.Property(profile => profile.ExternalAvatarUrl)
            .HasColumnName("external_avatar_url")
            .HasMaxLength(1000);

        builder.Property(profile => profile.AvatarSource)
            .HasColumnName("avatar_source")
            .HasConversion<int>()
            .HasDefaultValue(AvatarSourceEnum.None)
            .IsRequired();

        builder.Property(profile => profile.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(profile => profile.BirthDate)
            .HasColumnName("birth_date")
            .HasColumnType("date");

        builder.Property(profile => profile.TimeZone)
            .HasColumnName("time_zone")
            .HasMaxLength(100);

        builder.Property(profile => profile.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(profile => profile.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(profile => profile.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(profile => profile.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(profile => profile.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(profile => profile.AccountId).IsUnique();
        builder.HasIndex(profile => profile.AvatarFileId).HasFilter("\"avatar_file_id\" IS NOT NULL");
        builder.HasIndex(profile => profile.IsDeleted);

        builder.HasOne(profile => profile.Account)
            .WithOne(account => account.Profile)
            .HasForeignKey<AccountProfile>(profile => profile.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(profile => !profile.IsDeleted);

        builder.Ignore(profile => profile.DomainEvents);
    }
}
