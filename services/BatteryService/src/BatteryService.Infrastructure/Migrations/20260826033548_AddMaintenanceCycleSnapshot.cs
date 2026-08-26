using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceCycleSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alert_count",
                table: "maintenance_cycles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "avg_temperature_celsius",
                table: "maintenance_cycles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "critical_alert_count",
                table: "maintenance_cycles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cycle_count_delta",
                table: "maintenance_cycles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_temperature_celsius",
                table: "maintenance_cycles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_voltage",
                table: "maintenance_cycles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_voltage",
                table: "maintenance_cycles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reading_count",
                table: "maintenance_cycles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alert_count",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "avg_temperature_celsius",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "critical_alert_count",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "cycle_count_delta",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "max_temperature_celsius",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "max_voltage",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "min_voltage",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "reading_count",
                table: "maintenance_cycles");
        }
    }
}
