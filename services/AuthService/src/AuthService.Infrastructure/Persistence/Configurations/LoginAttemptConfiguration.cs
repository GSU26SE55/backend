using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Persistence.Configurations;

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts");

        builder.HasKey(la => la.Id);

        builder.Property(la => la.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(la => la.AccountId)
            .HasColumnName("account_id");

        builder.Property(la => la.AttemptedEmail)
            .HasColumnName("attempted_email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(la => la.Result)
            .HasColumnName("result")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(la => la.Method)
            .HasColumnName("method")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(la => la.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(la => la.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500);

        builder.Property(la => la.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(128);

        builder.Property(la => la.Note)
            .HasColumnName("note")
            .HasMaxLength(500);

        builder.Property(la => la.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(la => la.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(la => la.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(la => la.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(la => la.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(la => la.AccountId)
            .HasDatabaseName("ix_login_attempts_account_id");

        builder.HasIndex(la => new { la.AccountId, la.CreatedAt })
            .HasDatabaseName("ix_login_attempts_account_created_at");

        builder.HasIndex(la => la.AttemptedEmail)
            .HasDatabaseName("ix_login_attempts_attempted_email");

        builder.HasIndex(la => la.IpAddress)
            .HasDatabaseName("ix_login_attempts_ip_address");

        builder.HasOne(la => la.Account)
            .WithMany()
            .HasForeignKey(la => la.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(la => !la.IsDeleted);

        builder.Ignore(la => la.DomainEvents);
    }
}
