using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        builder.ToTable("blog_posts");

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(e => e.Slug).HasColumnName("slug").HasMaxLength(300).IsRequired();
        builder.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ContentHtml).HasColumnName("content_html").HasColumnType("text").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(e => e.Origin).HasColumnName("origin").HasConversion<int>();
        builder.Property(e => e.SourceKbArticleId).HasColumnName("source_kb_article_id");
        builder.Property(e => e.BlogTemplateId).HasColumnName("blog_template_id");
        builder.Property(e => e.AuthorUserId).HasColumnName("author_user_id");
        builder.Property(e => e.CurrentVersion).HasColumnName("current_version");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.SourceKbArticleId);

        builder.HasOne(e => e.SourceKbArticle)
            .WithMany()
            .HasForeignKey(e => e.SourceKbArticleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.BlogTemplate)
            .WithMany(t => t.BlogPosts)
            .HasForeignKey(e => e.BlogTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Versions)
            .WithOne(v => v.BlogPost)
            .HasForeignKey(v => v.BlogPostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
