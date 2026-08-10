using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketAiSuggestionConfiguration : IEntityTypeConfiguration<TicketAiSuggestion>
{
    public void Configure(EntityTypeBuilder<TicketAiSuggestion> builder)
    {
        builder.ToTable("ticket_ai_suggestions");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id")
            .IsRequired();

        builder.Property(e => e.Prescription)
            .HasColumnName("prescription")
            .IsRequired();

        // Danh sách chuỗi → jsonb, cùng pattern KnowledgeBaseArticle.Tags / StaffAccount.SkillCodes.
        builder.Property(e => e.ActionSteps)
            .HasColumnName("action_steps")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.PpeRequired)
            .HasColumnName("ppe_required")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.SopReferences)
            .HasColumnName("sop_references")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.EscalationConditions)
            .HasColumnName("escalation_conditions")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.SafetyWarnings)
            .HasColumnName("safety_warnings")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.KbDocRefs)
            .HasColumnName("kb_doc_refs")
            .HasColumnType("jsonb")
            .HasConversion(new JsonValueConverter<List<string>>());

        builder.Property(e => e.HumanVerificationRequired)
            .HasColumnName("human_verification_required")
            .HasDefaultValue(false);

        builder.Property(e => e.Blocked)
            .HasColumnName("blocked")
            .HasDefaultValue(false);

        builder.Property(e => e.Enriched)
            .HasColumnName("enriched")
            .HasDefaultValue(false);

        builder.Property(e => e.LlmProvider)
            .HasColumnName("llm_provider")
            .HasMaxLength(50);

        builder.Property(e => e.PrescriptionId)
            .HasColumnName("prescription_id")
            .HasMaxLength(64);

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

        // 1-1: mỗi ticket nhiều nhất 1 bản gợi ý. Unique cũng là chốt chặn idempotency —
        // consumer retry (MassTransit redelivery) sẽ vỡ ở đây thay vì ghi trùng âm thầm.
        builder.HasIndex(e => e.TicketId).IsUnique();

        builder.HasOne(e => e.Ticket)
            .WithOne()
            .HasForeignKey<TicketAiSuggestion>(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
