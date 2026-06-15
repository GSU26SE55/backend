using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmsService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial_SmsGateway_Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sms_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sms_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<int>(name: "event", type: "integer", nullable: false),
                    device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sms_gateway_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    daily_limit = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    sent_today = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    sent_today_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_gateway_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sms_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_service = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    gateway_device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    gateway_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    picked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    redacted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sms_audit_logs_sms_message_id",
                table: "sms_audit_logs",
                column: "sms_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_sms_audit_logs_sms_message_id_created_at",
                table: "sms_audit_logs",
                columns: new[] { "sms_message_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_sms_gateway_devices_device_code",
                table: "sms_gateway_devices",
                column: "device_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_correlation_id",
                table: "sms_messages",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_phone_number",
                table: "sms_messages",
                column: "phone_number");

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_status_created_at",
                table: "sms_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_status_sent_at",
                table: "sms_messages",
                columns: new[] { "status", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_target_status_created_at",
                table: "sms_messages",
                columns: new[] { "target_device_code", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "sms_audit_logs");

            migrationBuilder.DropTable(
                name: "sms_gateway_devices");

            migrationBuilder.DropTable(
                name: "sms_messages");
        }
    }
}
