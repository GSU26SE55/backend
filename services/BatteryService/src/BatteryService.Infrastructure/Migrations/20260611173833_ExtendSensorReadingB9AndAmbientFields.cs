using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSensorReadingB9AndAmbientFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bms_error_code",
                table: "sensor_readings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sensor_source_code",
                table: "sensor_readings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_type",
                table: "sensor_readings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "promoted_to_alert_id",
                table: "noise_breach_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_type",
                table: "noise_breach_events",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // PostgreSQL không tự cast varchar → integer; dùng USING với mapping
            // string values cũ ("IotSensor", "WeatherApi", ...) sang enum int (1, 2).
            migrationBuilder.Sql(@"
                ALTER TABLE ambient_readings
                ALTER COLUMN source DROP DEFAULT;
                ALTER TABLE ambient_readings
                ALTER COLUMN source TYPE integer
                USING (CASE
                    WHEN source IS NULL THEN 2
                    WHEN source ~ '^[0-9]+$' THEN source::integer
                    WHEN lower(source) IN ('iotsensor', 'iot', 'sensor') THEN 1
                    WHEN lower(source) IN ('weatherapi', 'weather', 'openmeteo') THEN 2
                    ELSE 2
                END);
                ALTER TABLE ambient_readings
                ALTER COLUMN source SET DEFAULT 2;
                ALTER TABLE ambient_readings
                ALTER COLUMN source SET NOT NULL;
            ");

            migrationBuilder.AlterColumn<decimal>(
                name: "humidity_percent",
                table: "ambient_readings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "solar_irradiance_wm2",
                table: "ambient_readings",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_device_id",
                table: "ambient_readings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_noise_breach_events_promoted_alert",
                table: "noise_breach_events",
                column: "promoted_to_alert_id",
                filter: "promoted_to_alert_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_noise_breach_events_promoted_alert",
                table: "noise_breach_events");

            migrationBuilder.DropColumn(
                name: "bms_error_code",
                table: "sensor_readings");

            migrationBuilder.DropColumn(
                name: "sensor_source_code",
                table: "sensor_readings");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "sensor_readings");

            migrationBuilder.DropColumn(
                name: "promoted_to_alert_id",
                table: "noise_breach_events");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "noise_breach_events");

            migrationBuilder.DropColumn(
                name: "solar_irradiance_wm2",
                table: "ambient_readings");

            migrationBuilder.DropColumn(
                name: "source_device_id",
                table: "ambient_readings");

            // Reverse cast integer → varchar với mapping ngược.
            migrationBuilder.Sql(@"
                ALTER TABLE ambient_readings
                ALTER COLUMN source DROP DEFAULT;
                ALTER TABLE ambient_readings
                ALTER COLUMN source TYPE character varying(50)
                USING (CASE source
                    WHEN 1 THEN 'IotSensor'
                    WHEN 2 THEN 'WeatherApi'
                    ELSE 'WeatherApi'
                END);
                ALTER TABLE ambient_readings
                ALTER COLUMN source SET NOT NULL;
            ");

            migrationBuilder.AlterColumn<decimal>(
                name: "humidity_percent",
                table: "ambient_readings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
