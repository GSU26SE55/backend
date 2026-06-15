using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertTicketSagaFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_ticket_saga_states",
                columns: table => new
                {
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    anomaly_type = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    threshold_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    internal_resistance_milliohm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    cell_voltage_delta_mv = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    environmental_incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ticket_is_reused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    failed_at_stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failure_error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pending_timeout_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_ticket_saga_states", x => x.correlation_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_ticket_saga_states_battery_asset_id",
                table: "alert_ticket_saga_states",
                column: "battery_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ticket_saga_states_current_state",
                table: "alert_ticket_saga_states",
                column: "current_state");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ticket_saga_states_started_at",
                table: "alert_ticket_saga_states",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ticket_saga_states_ticket_id",
                table: "alert_ticket_saga_states",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ux_alert_ticket_saga_states_alert_id",
                table: "alert_ticket_saga_states",
                column: "alert_id",
                unique: true);

            // Unique filtered index trên tickets.origin_alert_id — 1 Alert chỉ map tới 1 Ticket gốc
            // (overall.md §53.6). Filter loại NULL + soft-deleted.
            // PHẢI chạy sau preflight cleanup (runbook 10-saga-duplicate-canonical.md).
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_origin_alert_id
ON tickets (origin_alert_id)
WHERE origin_alert_id IS NOT NULL AND is_deleted = false;");

            // Partial unique guard — chống 2 auto-Ticket active cùng (battery_asset_id, category).
            // Active = status NOT IN (Resolved=6, ClosedPendingRate=7, Closed=8, ClosedRejected=9).
            // Note: Origin = Auto (1) per TicketOriginEnum.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
ON tickets (battery_asset_id, category)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8, 9);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_origin_alert_id;");

            migrationBuilder.DropTable(
                name: "alert_ticket_saga_states");
        }
    }
}
