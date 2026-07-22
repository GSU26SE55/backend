using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class KnowledgeBaseArticleConfiguration : IEntityTypeConfiguration<KnowledgeBaseArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseArticle> builder)
    {
        builder.ToTable("knowledge_base_articles");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasConversion<int>();

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Content)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .HasConversion(new JsonDocumentValueConverter())
            .IsRequired();

        builder.Property(e => e.Tags)
            .HasColumnName("tags")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(e => e.Version)
            .HasColumnName("version");

        builder.Property(e => e.ViewCount)
            .HasColumnName("view_count");

        builder.Property(e => e.HelpfulCount)
            .HasColumnName("helpful_count");

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(e => e.PendingReviewBy)
            .HasColumnName("pending_review_by");

        builder.Property(e => e.ReviewRequired)
            .HasColumnName("review_required")
            .HasDefaultValue(false);

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

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Versions)
            .WithOne(e => e.Article)
            .HasForeignKey(e => e.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
