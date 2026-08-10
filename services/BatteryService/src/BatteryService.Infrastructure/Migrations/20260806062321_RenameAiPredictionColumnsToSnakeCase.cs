using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAiPredictionColumnsToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StageConfidence",
                table: "soh_predictions",
                newName: "stage_confidence");

            migrationBuilder.RenameColumn(
                name: "SohTrend",
                table: "soh_predictions",
                newName: "soh_trend");

            migrationBuilder.RenameColumn(
                name: "SohStd",
                table: "soh_predictions",
                newName: "soh_std");

            migrationBuilder.RenameColumn(
                name: "RulCyclesEstimate",
                table: "soh_predictions",
                newName: "rul_cycles_estimate");

            migrationBuilder.RenameColumn(
                name: "RiskLevel",
                table: "soh_predictions",
                newName: "risk_level");

            migrationBuilder.RenameColumn(
                name: "IsTemperatureOod",
                table: "soh_predictions",
                newName: "is_temperature_ood");

            migrationBuilder.RenameColumn(
                name: "IsBorderline",
                table: "soh_predictions",
                newName: "is_borderline");

            migrationBuilder.RenameColumn(
                name: "HealthStage",
                table: "soh_predictions",
                newName: "health_stage");

            migrationBuilder.RenameColumn(
                name: "DegradationRatePerCycle",
                table: "soh_predictions",
                newName: "degradation_rate_per_cycle");

            migrationBuilder.RenameColumn(
                name: "CyclesToMaintenance",
                table: "soh_predictions",
                newName: "cycles_to_maintenance");

            migrationBuilder.RenameColumn(
                name: "AiPriority",
                table: "soh_predictions",
                newName: "ai_priority");

            migrationBuilder.RenameColumn(
                name: "ActionCode",
                table: "soh_predictions",
                newName: "action_code");

            migrationBuilder.AlterColumn<decimal>(
                name: "stage_confidence",
                table: "soh_predictions",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "soh_trend",
                table: "soh_predictions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "soh_std",
                table: "soh_predictions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "risk_level",
                table: "soh_predictions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_temperature_ood",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_borderline",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "health_stage",
                table: "soh_predictions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "degradation_rate_per_cycle",
                table: "soh_predictions",
                type: "numeric(8,5)",
                precision: 8,
                scale: 5,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ai_priority",
                table: "soh_predictions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "action_code",
                table: "soh_predictions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "stage_confidence",
                table: "soh_predictions",
                newName: "StageConfidence");

            migrationBuilder.RenameColumn(
                name: "soh_trend",
                table: "soh_predictions",
                newName: "SohTrend");

            migrationBuilder.RenameColumn(
                name: "soh_std",
                table: "soh_predictions",
                newName: "SohStd");

            migrationBuilder.RenameColumn(
                name: "rul_cycles_estimate",
                table: "soh_predictions",
                newName: "RulCyclesEstimate");

            migrationBuilder.RenameColumn(
                name: "risk_level",
                table: "soh_predictions",
                newName: "RiskLevel");

            migrationBuilder.RenameColumn(
                name: "is_temperature_ood",
                table: "soh_predictions",
                newName: "IsTemperatureOod");

            migrationBuilder.RenameColumn(
                name: "is_borderline",
                table: "soh_predictions",
                newName: "IsBorderline");

            migrationBuilder.RenameColumn(
                name: "health_stage",
                table: "soh_predictions",
                newName: "HealthStage");

            migrationBuilder.RenameColumn(
                name: "degradation_rate_per_cycle",
                table: "soh_predictions",
                newName: "DegradationRatePerCycle");

            migrationBuilder.RenameColumn(
                name: "cycles_to_maintenance",
                table: "soh_predictions",
                newName: "CyclesToMaintenance");

            migrationBuilder.RenameColumn(
                name: "ai_priority",
                table: "soh_predictions",
                newName: "AiPriority");

            migrationBuilder.RenameColumn(
                name: "action_code",
                table: "soh_predictions",
                newName: "ActionCode");

            migrationBuilder.AlterColumn<decimal>(
                name: "StageConfidence",
                table: "soh_predictions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,3)",
                oldPrecision: 4,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SohTrend",
                table: "soh_predictions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "SohStd",
                table: "soh_predictions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RiskLevel",
                table: "soh_predictions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsTemperatureOod",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "IsBorderline",
                table: "soh_predictions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "HealthStage",
                table: "soh_predictions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DegradationRatePerCycle",
                table: "soh_predictions",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,5)",
                oldPrecision: 8,
                oldScale: 5,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AiPriority",
                table: "soh_predictions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActionCode",
                table: "soh_predictions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
