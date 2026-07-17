using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class BlogPostVersionConfiguration : IEntityTypeConfiguration<BlogPostVersion>
{
    public void Configure(EntityTypeBuilder<BlogPostVersion> builder)
    {
        builder.ToTable("blog_post_versions");

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.BlogPostId).HasColumnName("blog_post_id");
        builder.Property(e => e.VersionNumber).HasColumnName("version_number");
        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ContentHtml).HasColumnName("content_html").HasColumnType("text").IsRequired();
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(e => e.ChangeNote).HasColumnName("change_note").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(e => e.BlogPostId);
        builder.HasIndex(e => new { e.BlogPostId, e.VersionNumber }).IsUnique();
    }
}
