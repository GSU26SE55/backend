using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Persistence.Configurations;

public class TicketBatteryAssetConfiguration : IEntityTypeConfiguration<TicketBatteryAsset>
{
    public void Configure(EntityTypeBuilder<TicketBatteryAsset> builder)
    {
        builder.ToTable("ticket_battery_assets");

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(e => e.BatteryAssetId)
            .HasColumnName("battery_asset_id");

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

        builder.HasOne(e => e.Ticket)
            .WithMany(t => t.BatteryAssets)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TicketId);
        builder.HasIndex(e => e.BatteryAssetId);
        builder.HasIndex(e => new { e.TicketId, e.BatteryAssetId }).IsUnique();
    }
}
