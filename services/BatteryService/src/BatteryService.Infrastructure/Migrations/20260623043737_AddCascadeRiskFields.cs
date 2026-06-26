using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCascadeRiskFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cascade_risk_score",
                table: "battery_assets",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "cascade_risk_updated_at",
                table: "battery_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "electrical_topology",
                table: "battery_assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cascade_risk_score",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "cascade_risk_updated_at",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "electrical_topology",
                table: "battery_assets");
        }
    }
}
