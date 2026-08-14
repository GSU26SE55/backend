using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDeviceOfflineIncidentGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "iot_device_id",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_alerts_open_device_offline_incident",
                table: "alerts",
                column: "iot_device_id",
                unique: true,
                filter: "iot_device_id IS NOT NULL AND anomaly_type = 7 AND status IN (1, 2) AND is_deleted = false");

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_iot_devices_iot_device_id",
                table: "alerts",
                column: "iot_device_id",
                principalTable: "iot_devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alerts_iot_devices_iot_device_id",
                table: "alerts");

            migrationBuilder.DropIndex(
                name: "ux_alerts_open_device_offline_incident",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "iot_device_id",
                table: "alerts");
        }
    }
}
