using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class KbArticleVersionConfiguration : IEntityTypeConfiguration<KbArticleVersion>
{
    public void Configure(EntityTypeBuilder<KbArticleVersion> builder)
    {
        builder.ToTable("kb_article_versions");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.ArticleId)
            .HasColumnName("article_id");

        builder.Property(e => e.MajorVersion)
            .HasColumnName("major_version");

        builder.Property(e => e.MinorVersion)
            .HasColumnName("minor_version");

        builder.Property(e => e.Status)
            .HasColumnName("status");

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Symptoms)
            .HasColumnName("symptoms")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.DiagnosisSteps)
            .HasColumnName("diagnosis_steps")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.SolutionSteps)
            .HasColumnName("solution_steps")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.RecommendedParts)
            .HasColumnName("recommended_parts")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>?>());

        builder.Property(e => e.Tags)
            .HasColumnName("tags")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.ChangeDescription)
            .HasColumnName("change_description")
            .HasMaxLength(500);

        builder.Property(e => e.ChangedBy)
            .HasColumnName("changed_by");

        builder.Property(e => e.ManagerRejectReason)
            .HasColumnName("manager_reject_reason");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(e => e.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted");

        builder.Property(e => e.DeletedAt)
            .HasColumnName("deleted_at");

        builder.HasIndex(e => e.ArticleId);
        builder.HasIndex(e => e.MajorVersion);
        builder.HasIndex(e => new { e.ArticleId, e.MajorVersion, e.MinorVersion }).IsUnique();
    }
}
