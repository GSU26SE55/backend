using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryService.Infrastructure.Persistence.Configurations;

public class IotDeviceCommandConfiguration : IEntityTypeConfiguration<IotDeviceCommand>
{
    public void Configure(EntityTypeBuilder<IotDeviceCommand> builder)
    {
        builder.ToTable("iot_device_commands");
        builder.HasKey(command => command.Id);

        builder.Property(command => command.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(command => command.IotDeviceId).HasColumnName("iot_device_id").IsRequired();
        builder.Property(command => command.BatteryAssetId).HasColumnName("battery_asset_id");
        builder.Property(command => command.CmdId).HasColumnName("cmd_id").HasMaxLength(64).IsRequired();
        builder.Property(command => command.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
        builder.Property(command => command.ParamsJson).HasColumnName("params_json").HasColumnType("jsonb").IsRequired();
        builder.Property(command => command.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(IotDeviceCommandStatusEnum.Pending)
            .IsRequired();
        builder.Property(command => command.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        builder.Property(command => command.AckError).HasColumnName("ack_error").HasMaxLength(512);
        builder.Property(command => command.AckedAt).HasColumnName("acked_at");
        builder.Property(command => command.IssuedByAccountId).HasColumnName("issued_by_account_id");

        builder.Property(command => command.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(command => command.CreatedBy).HasColumnName("created_by");
        builder.Property(command => command.UpdatedAt).HasColumnName("updated_at");
        builder.Property(command => command.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(command => command.DeletedAt).HasColumnName("deleted_at");

        builder.HasOne(command => command.IotDevice)
            .WithMany(device => device.Commands)
            .HasForeignKey(command => command.IotDeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(command => command.BatteryAsset)
            .WithMany(asset => asset.IotDeviceCommands)
            .HasForeignKey(command => command.BatteryAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(command => command.CmdId)
            .IsUnique()
            .HasDatabaseName("idx_iot_device_commands_cmd_id");
        builder.HasIndex(command => new { command.BatteryAssetId, command.Status, command.Type })
            .HasDatabaseName("idx_iot_device_commands_asset_status_type");
        builder.HasIndex(command => new { command.IotDeviceId, command.CreatedAt })
            .HasDatabaseName("idx_iot_device_commands_device_created");

        builder.Ignore(command => command.DomainEvents);
    }
}
