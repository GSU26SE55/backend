using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    action_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_display = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    actor_display = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    actor_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_success = table.Column<bool>(type: "boolean", nullable: false),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_audit_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_audit_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_audit_logs_action",
                table: "notification_audit_logs",
                columns: new[] { "action_code", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_audit_logs_event_id",
                table: "notification_audit_logs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_audit_outbox_pending",
                table: "notification_audit_outbox",
                columns: new[] { "status", "created_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ux_notification_audit_outbox_event_id",
                table: "notification_audit_outbox",
                column: "event_id",
                unique: true);

            // #AUDIT-34 — trigger append-only soft mode cho notification_audit_logs.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION enforce_notification_audit_logs_append_only_soft()
RETURNS TRIGGER AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        RAISE EXCEPTION 'notification_audit_logs is append-only; DELETE is not allowed';
    END IF;
    IF (NEW.event_id IS DISTINCT FROM OLD.event_id
        OR NEW.action_code IS DISTINCT FROM OLD.action_code
        OR NEW.actor_account_id IS DISTINCT FROM OLD.actor_account_id
        OR NEW.target_id IS DISTINCT FROM OLD.target_id
        OR NEW.occurred_at IS DISTINCT FROM OLD.occurred_at) THEN
        RAISE EXCEPTION 'notification_audit_logs business fields are immutable';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER notification_audit_logs_append_only_soft
BEFORE UPDATE OR DELETE ON notification_audit_logs
FOR EACH ROW EXECUTE FUNCTION enforce_notification_audit_logs_append_only_soft();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS notification_audit_logs_append_only_soft ON notification_audit_logs;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS enforce_notification_audit_logs_append_only_soft();");

            migrationBuilder.DropTable(
                name: "notification_audit_logs");

            migrationBuilder.DropTable(
                name: "notification_audit_outbox");
        }
    }
}
