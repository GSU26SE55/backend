using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPredictionDetailColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionCode",
                table: "soh_predictions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiPriority",
                table: "soh_predictions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CyclesToMaintenance",
                table: "soh_predictions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DegradationRatePerCycle",
                table: "soh_predictions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthStage",
                table: "soh_predictions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBorderline",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemperatureOod",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "soh_predictions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RulCyclesEstimate",
                table: "soh_predictions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SohStd",
                table: "soh_predictions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SohTrend",
                table: "soh_predictions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StageConfidence",
                table: "soh_predictions",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionCode",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "AiPriority",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "CyclesToMaintenance",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "DegradationRatePerCycle",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "HealthStage",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "IsBorderline",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "IsTemperatureOod",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "RulCyclesEstimate",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "SohStd",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "SohTrend",
                table: "soh_predictions");

            migrationBuilder.DropColumn(
                name: "StageConfidence",
                table: "soh_predictions");
        }
    }
}
