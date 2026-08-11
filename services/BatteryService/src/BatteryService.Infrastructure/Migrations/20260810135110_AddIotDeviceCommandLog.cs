using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDeviceCommandLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iot_device_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iot_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cmd_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    ack_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    acked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    issued_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_device_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_iot_device_commands_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_iot_device_commands_iot_devices_iot_device_id",
                        column: x => x.iot_device_id,
                        principalTable: "iot_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_iot_device_commands_asset_status_type",
                table: "iot_device_commands",
                columns: new[] { "battery_asset_id", "status", "type" });

            migrationBuilder.CreateIndex(
                name: "idx_iot_device_commands_cmd_id",
                table: "iot_device_commands",
                column: "cmd_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_iot_device_commands_device_created",
                table: "iot_device_commands",
                columns: new[] { "iot_device_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iot_device_commands");
        }
    }
}
