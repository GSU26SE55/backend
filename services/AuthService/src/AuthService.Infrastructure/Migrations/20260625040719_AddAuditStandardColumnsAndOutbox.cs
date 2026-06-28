using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditStandardColumnsAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "action_category",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "action_code",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "actor_display",
                table: "audit_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_role",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_code",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "occurred_at",
                table: "audit_logs",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "recorded_at",
                table: "audit_logs",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "service_name",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "severity",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "target_display",
                table: "audit_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_type",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_outbox",
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
                    table.PrimaryKey("PK_audit_outbox", x => x.id);
                });

            // #AUDIT-10 — drop hard trigger AUTH-29 để backfill chạy được (trigger chặn mọi UPDATE audit_logs).
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;");

            // #AUDIT-06 — backfill row cũ TRƯỚC khi tạo unique index event_id (tránh collision Guid.Empty).
            migrationBuilder.Sql(@"
UPDATE audit_logs SET event_id = gen_random_uuid()
    WHERE event_id = '00000000-0000-0000-0000-000000000000';
UPDATE audit_logs SET
    service_name    = COALESCE(NULLIF(service_name, ''), 'AuthService'),
    action_code     = COALESCE(NULLIF(action_code, ''), 'Action_' || action::text),
    action_category = COALESCE(NULLIF(action_category, ''), 'AccountManagement'),
    severity        = COALESCE(NULLIF(severity, ''), CASE WHEN is_success THEN 'Info' ELSE 'Warning' END),
    occurred_at     = CASE WHEN occurred_at <= '0001-01-02'::timestamptz THEN created_at ELSE occurred_at END,
    recorded_at     = CASE WHEN recorded_at <= '0001-01-02'::timestamptz THEN created_at ELSE recorded_at END
    WHERE service_name = '' OR action_code = '' OR occurred_at <= '0001-01-02'::timestamptz;
");

            migrationBuilder.CreateIndex(
                name: "ux_audit_logs_event_id",
                table: "audit_logs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_outbox_pending",
                table: "audit_outbox",
                columns: new[] { "status", "created_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ux_audit_outbox_event_id",
                table: "audit_outbox",
                column: "event_id",
                unique: true);

            // #AUDIT-10 — tạo lại trigger SOFT MODE: chặn DELETE + chặn UPDATE các business field
            // (action_code, actor_account_id, target_id, occurred_at, event_id); cho phép UPDATE field khác
            // (vd GDPR redact PII display). Phụ lục B §B.9.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION enforce_audit_logs_append_only_soft()
RETURNS TRIGGER AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        RAISE EXCEPTION 'audit_logs is append-only; DELETE is not allowed';
    END IF;
    IF (NEW.event_id IS DISTINCT FROM OLD.event_id
        OR NEW.action_code IS DISTINCT FROM OLD.action_code
        OR NEW.actor_account_id IS DISTINCT FROM OLD.actor_account_id
        OR NEW.target_id IS DISTINCT FROM OLD.target_id
        OR NEW.occurred_at IS DISTINCT FROM OLD.occurred_at) THEN
        RAISE EXCEPTION 'audit_logs business fields are immutable (event_id/action_code/actor_account_id/target_id/occurred_at)';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;");
            migrationBuilder.Sql(@"
CREATE TRIGGER audit_logs_append_only_soft
BEFORE UPDATE OR DELETE ON audit_logs
FOR EACH ROW
EXECUTE FUNCTION enforce_audit_logs_append_only_soft();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // #AUDIT-10 rollback — khôi phục hard trigger AUTH-29, bỏ soft trigger.
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_append_only_soft ON audit_logs;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS enforce_audit_logs_append_only_soft();");
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION enforce_audit_logs_append_only()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only; % is not allowed', TG_OP;
                END;
                $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER audit_logs_no_update_delete
                BEFORE UPDATE OR DELETE ON audit_logs
                FOR EACH ROW
                EXECUTE FUNCTION enforce_audit_logs_append_only();");

            migrationBuilder.DropTable(
                name: "audit_outbox");

            migrationBuilder.DropIndex(
                name: "ux_audit_logs_event_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "action_category",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "action_code",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor_display",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor_role",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "causation_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "error_code",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "occurred_at",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "recorded_at",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "service_name",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "severity",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "target_display",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "target_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "target_type",
                table: "audit_logs");
        }
    }
}
