using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAmbientMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cell_voltage_delta_max_mv",
                table: "threshold_configs",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "internal_resistance_max_milliohm",
                table: "threshold_configs",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "noise_suppression_count",
                table: "threshold_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "noise_suppression_enabled",
                table: "threshold_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "noise_suppression_window_hours",
                table: "threshold_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cell_voltage_delta_mv",
                table: "sensor_readings",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "internal_resistance_milliohm",
                table: "sensor_readings",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "battery_asset_id",
                table: "alerts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "environmental_incident_id",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "site_id",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ambient_readings",
                columns: table => new
                {
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambient_temperature_celsius = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    humidity_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ambient_readings", x => new { x.time, x.site_id });
                    table.ForeignKey(
                        name: "FK_ambient_readings_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ambient_threshold_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    high_ambient_temp_warning = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    high_ambient_temp_critical = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    high_humidity_warning = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    high_humidity_critical = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    combo_temp_threshold = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    combo_humidity_threshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ambient_threshold_configs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ambient_threshold_configs_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "environmental_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    severity = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    reported_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    false_alarm_by = table.Column<Guid>(type: "uuid", nullable: true),
                    false_alarm_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    false_alarm_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environmental_incidents", x => x.id);
                    table.ForeignKey(
                        name: "FK_environmental_incidents_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "noise_breach_events",
                columns: table => new
                {
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anomaly_type = table.Column<int>(type: "integer", nullable: false),
                    threshold_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_noise_breach_events", x => new { x.time, x.battery_asset_id, x.anomaly_type });
                    table.ForeignKey(
                        name: "FK_noise_breach_events_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_environmental_incident_id",
                table: "alerts",
                column: "environmental_incident_id");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_site_id",
                table: "alerts",
                column: "site_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alerts_asset_or_site_required",
                table: "alerts",
                sql: "battery_asset_id IS NOT NULL OR site_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ambient_readings_site_time",
                table: "ambient_readings",
                columns: new[] { "site_id", "time" });

            migrationBuilder.CreateIndex(
                name: "ux_ambient_threshold_configs_site_active",
                table: "ambient_threshold_configs",
                column: "site_id",
                unique: true,
                filter: "is_deleted = false AND enabled = true");

            migrationBuilder.CreateIndex(
                name: "ix_environmental_incidents_detected_at",
                table: "environmental_incidents",
                column: "detected_at");

            migrationBuilder.CreateIndex(
                name: "ix_environmental_incidents_site_status",
                table: "environmental_incidents",
                columns: new[] { "site_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_noise_breach_events_asset_type_time",
                table: "noise_breach_events",
                columns: new[] { "battery_asset_id", "anomaly_type", "time" });

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_environmental_incidents_environmental_incident_id",
                table: "alerts",
                column: "environmental_incident_id",
                principalTable: "environmental_incidents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_sites_site_id",
                table: "alerts",
                column: "site_id",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Sprint 5B #89/B1 — TimescaleDB hypertable cho time-series tables.
            // IF NOT EXISTS guard cho idempotent re-apply.
            migrationBuilder.Sql(@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
    PERFORM create_hypertable('ambient_readings', 'time', if_not_exists => TRUE, migrate_data => TRUE);
    PERFORM create_hypertable('noise_breach_events', 'time', if_not_exists => TRUE, migrate_data => TRUE);
  END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alerts_environmental_incidents_environmental_incident_id",
                table: "alerts");

            migrationBuilder.DropForeignKey(
                name: "FK_alerts_sites_site_id",
                table: "alerts");

            migrationBuilder.DropTable(
                name: "ambient_readings");

            migrationBuilder.DropTable(
                name: "ambient_threshold_configs");

            migrationBuilder.DropTable(
                name: "environmental_incidents");

            migrationBuilder.DropTable(
                name: "noise_breach_events");

            migrationBuilder.DropIndex(
                name: "IX_alerts_environmental_incident_id",
                table: "alerts");

            migrationBuilder.DropIndex(
                name: "IX_alerts_site_id",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_alerts_asset_or_site_required",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "cell_voltage_delta_max_mv",
                table: "threshold_configs");

            migrationBuilder.DropColumn(
                name: "internal_resistance_max_milliohm",
                table: "threshold_configs");

            migrationBuilder.DropColumn(
                name: "noise_suppression_count",
                table: "threshold_configs");

            migrationBuilder.DropColumn(
                name: "noise_suppression_enabled",
                table: "threshold_configs");

            migrationBuilder.DropColumn(
                name: "noise_suppression_window_hours",
                table: "threshold_configs");

            migrationBuilder.DropColumn(
                name: "cell_voltage_delta_mv",
                table: "sensor_readings");

            migrationBuilder.DropColumn(
                name: "internal_resistance_milliohm",
                table: "sensor_readings");

            migrationBuilder.DropColumn(
                name: "environmental_incident_id",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "site_id",
                table: "alerts");

            migrationBuilder.AlterColumn<Guid>(
                name: "battery_asset_id",
                table: "alerts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
