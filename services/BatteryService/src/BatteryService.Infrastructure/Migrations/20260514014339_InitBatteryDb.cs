using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitBatteryDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "battery_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nominal_capacity_ah = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    nominal_voltage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    chemistry = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    max_cycle_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 2000),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_battery_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "battery_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    battery_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    install_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    warranty_end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    warranty_status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_sensor_reading_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_battery_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_battery_assets_battery_types_battery_type_id",
                        column: x => x.battery_type_id,
                        principalTable: "battery_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "threshold_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voltage_min = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    voltage_max = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    temperature_max = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    temperature_min = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    soc_warning_threshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    soc_critical_threshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    current_max_charge = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    current_max_discharge = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threshold_configs", x => x.id);
                    table.ForeignKey(
                        name: "FK_threshold_configs_battery_types_battery_type_id",
                        column: x => x.battery_type_id,
                        principalTable: "battery_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anomaly_type = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    threshold_value = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    actual_value = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    merged_into_alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dedup_window_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_alerts_alerts_merged_into_alert_id",
                        column: x => x.merged_into_alert_id,
                        principalTable: "alerts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerts_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sensor_readings",
                columns: table => new
                {
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voltage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    current = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    soc_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    cycle_count = table.Column<int>(type: "integer", nullable: true),
                    source_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_readings", x => new { x.time, x.battery_asset_id });
                    table.ForeignKey(
                        name: "FK_sensor_readings_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_battery_asset_id",
                table: "alerts",
                column: "battery_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_battery_asset_id_anomaly_type_status_dedup_window_en~",
                table: "alerts",
                columns: new[] { "battery_asset_id", "anomaly_type", "status", "dedup_window_end_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_merged_into_alert_id",
                table: "alerts",
                column: "merged_into_alert_id");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_severity",
                table: "alerts",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_status",
                table: "alerts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_battery_type_id",
                table: "battery_assets",
                column: "battery_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_customer_id",
                table: "battery_assets",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_customer_id_is_deleted_status",
                table: "battery_assets",
                columns: new[] { "customer_id", "is_deleted", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_last_sensor_reading_at",
                table: "battery_assets",
                column: "last_sensor_reading_at");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_serial_number",
                table: "battery_assets",
                column: "serial_number",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_battery_assets_status",
                table: "battery_assets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_battery_types_chemistry",
                table: "battery_types",
                column: "chemistry");

            migrationBuilder.CreateIndex(
                name: "IX_battery_types_is_deleted",
                table: "battery_types",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_battery_types_name",
                table: "battery_types",
                column: "name",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_sensor_readings_asset_time",
                table: "sensor_readings",
                columns: new[] { "battery_asset_id", "time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_threshold_configs_battery_type_id",
                table: "threshold_configs",
                column: "battery_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_threshold_configs_battery_type_id_is_active",
                table: "threshold_configs",
                columns: new[] { "battery_type_id", "is_active" },
                unique: true,
                filter: "is_active = true AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "sensor_readings");

            migrationBuilder.DropTable(
                name: "threshold_configs");

            migrationBuilder.DropTable(
                name: "battery_assets");

            migrationBuilder.DropTable(
                name: "battery_types");
        }
    }
}
