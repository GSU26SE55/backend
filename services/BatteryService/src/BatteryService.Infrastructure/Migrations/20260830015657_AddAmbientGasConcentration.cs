using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAmbientGasConcentration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "high_gas_critical",
                table: "ambient_threshold_configs",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "high_gas_warning",
                table: "ambient_threshold_configs",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gas_concentration_percent",
                table: "ambient_readings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "high_gas_critical",
                table: "ambient_threshold_configs");

            migrationBuilder.DropColumn(
                name: "high_gas_warning",
                table: "ambient_threshold_configs");

            migrationBuilder.DropColumn(
                name: "gas_concentration_percent",
                table: "ambient_readings");
        }
    }
}
