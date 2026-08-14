using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnomalyClassificationAndSohPrediction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anomaly_classifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    classification = table.Column<int>(type: "integer", nullable: false),
                    anomaly_score = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    model_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    classified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    staff_feedback = table.Column<int>(type: "integer", nullable: true),
                    staff_feedback_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    staff_feedback_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anomaly_classifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_anomaly_classifications_alerts_alert_id",
                        column: x => x.alert_id,
                        principalTable: "alerts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_anomaly_classifications_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "soh_predictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predicted_soh_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    model_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    input_window_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    input_window_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    predicted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    raw_response = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soh_predictions", x => x.id);
                    table.ForeignKey(
                        name: "FK_soh_predictions_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anomaly_classifications_alert_id",
                table: "anomaly_classifications",
                column: "alert_id");

            migrationBuilder.CreateIndex(
                name: "ix_anomaly_classifications_asset_classified_at",
                table: "anomaly_classifications",
                columns: new[] { "battery_asset_id", "classified_at" });

            migrationBuilder.CreateIndex(
                name: "ix_soh_predictions_asset_predicted_at",
                table: "soh_predictions",
                columns: new[] { "battery_asset_id", "predicted_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anomaly_classifications");

            migrationBuilder.DropTable(
                name: "soh_predictions");
        }
    }
}
