using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class ChatAiSuggestionConfiguration : IEntityTypeConfiguration<ChatAiSuggestion>
{
    public void Configure(EntityTypeBuilder<ChatAiSuggestion> builder)
    {
        builder.ToTable("chat_ai_suggestions");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.SuggestedAt)
            .HasColumnName("suggested_at");

        builder.Property(e => e.Intent)
            .HasColumnName("intent")
            .HasConversion<int>();

        builder.Property(e => e.Suggestions)
            .HasColumnName("suggestions")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.SelectedIndex)
            .HasColumnName("selected_index");

        builder.Property(e => e.EditedBeforePost)
            .HasColumnName("edited_before_post");

        builder.Property(e => e.FinalChatId)
            .HasColumnName("final_chat_id");

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

        builder.HasIndex(e => e.TicketId)
            .HasDatabaseName("ix_chat_ai_suggestions_ticket_id");

        builder.HasIndex(e => e.FinalChatId)
            .HasDatabaseName("ix_chat_ai_suggestions_final_chat_id");

        builder.HasOne(e => e.Ticket)
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TicketChat>()
            .WithMany()
            .HasForeignKey(e => e.FinalChatId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
