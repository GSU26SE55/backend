using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotSprint2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel",
                table: "iot_firmware_releases",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "device_model",
                table: "iot_firmware_releases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_required",
                table: "iot_firmware_releases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "auto_decommissioned_at",
                table: "iot_devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mqtt_password_hash",
                table: "iot_devices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mqtt_username",
                table: "iot_devices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "outlier_incident_count",
                table: "iot_devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "outlier_window_started_at",
                table: "iot_devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sensor_ingest_idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    inserted = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    total_received = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_ingest_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_sensor_ingest_idempotency_expires_at",
                table: "sensor_ingest_idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_sensor_ingest_idempotency_device_key",
                table: "sensor_ingest_idempotency_records",
                columns: new[] { "device_code", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sensor_ingest_idempotency_records");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "iot_firmware_releases");

            migrationBuilder.DropColumn(
                name: "device_model",
                table: "iot_firmware_releases");

            migrationBuilder.DropColumn(
                name: "is_required",
                table: "iot_firmware_releases");

            migrationBuilder.DropColumn(
                name: "auto_decommissioned_at",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "mqtt_password_hash",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "mqtt_username",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "outlier_incident_count",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "outlier_window_started_at",
                table: "iot_devices");
        }
    }
}
