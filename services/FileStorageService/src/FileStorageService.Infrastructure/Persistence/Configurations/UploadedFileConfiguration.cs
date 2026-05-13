using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStorageService.Infrastructure.Persistence.Configurations;

public class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> builder)
    {
        builder.ToTable("uploaded_files");

        builder.HasKey(file => file.Id);

        builder.Property(file => file.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(file => file.ObjectKey)
            .HasColumnName("object_key")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(file => file.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(file => file.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(file => file.Size)
            .HasColumnName("size")
            .IsRequired();

        builder.Property(file => file.FolderName)
            .HasColumnName("folder_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(file => file.Purpose)
            .HasColumnName("purpose")
            .HasConversion<int>()
            .HasDefaultValue(FilePurposeEnum.Other)
            .IsRequired();

        builder.Property(file => file.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(FileStatusEnum.Uploaded)
            .IsRequired();

        builder.Property(file => file.PublicUrl)
            .HasColumnName("public_url")
            .HasMaxLength(1200);

        builder.Property(file => file.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(file => file.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(file => file.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(file => file.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(file => file.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(file => file.ObjectKey).IsUnique();
        builder.HasIndex(file => file.Purpose);
        builder.HasIndex(file => file.Status);
        builder.HasIndex(file => file.IsDeleted);

        builder.HasQueryFilter(file => !file.IsDeleted);

        builder.Ignore(file => file.DomainEvents);
    }
}
