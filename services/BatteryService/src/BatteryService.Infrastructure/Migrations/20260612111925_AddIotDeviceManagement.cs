using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDeviceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iot_firmware_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hardware_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sha256_checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_firmware_releases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iot_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hardware_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    current_firmware_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    target_firmware_release_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    api_key_last_four = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    api_key_scopes = table.Column<int>(type: "integer", nullable: false, defaultValue: 11),
                    api_key_issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    api_key_revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_provisioned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_offline_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    heartbeat_interval_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    last_clock_skew_seconds = table.Column<double>(type: "double precision", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_devices", x => x.id);
                    table.ForeignKey(
                        name: "FK_iot_devices_iot_firmware_releases_target_firmware_release_id",
                        column: x => x.target_firmware_release_id,
                        principalTable: "iot_firmware_releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_iot_devices_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iot_device_calibrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iot_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scale = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 1m),
                    offset_value = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    calibrated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_device_calibrations", x => x.id);
                    table.ForeignKey(
                        name: "FK_iot_device_calibrations_iot_devices_iot_device_id",
                        column: x => x.iot_device_id,
                        principalTable: "iot_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iot_device_heartbeats",
                columns: table => new
                {
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    iot_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firmware_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    rssi_dbm = table.Column<int>(type: "integer", nullable: true),
                    free_memory_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    uptime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    queued_reading_count = table.Column<int>(type: "integer", nullable: true),
                    device_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    clock_skew_seconds = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_device_heartbeats", x => new { x.time, x.iot_device_id });
                    table.ForeignKey(
                        name: "FK_iot_device_heartbeats_iot_devices_iot_device_id",
                        column: x => x.iot_device_id,
                        principalTable: "iot_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iot_firmware_update_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iot_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iot_firmware_release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    bytes_downloaded = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iot_firmware_update_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_iot_firmware_update_logs_iot_devices_iot_device_id",
                        column: x => x.iot_device_id,
                        principalTable: "iot_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_iot_firmware_update_logs_iot_firmware_releases_iot_firmware~",
                        column: x => x.iot_firmware_release_id,
                        principalTable: "iot_firmware_releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_iot_calibrations_lookup",
                table: "iot_device_calibrations",
                columns: new[] { "iot_device_id", "channel", "battery_asset_id" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_iot_heartbeats_device_time",
                table: "iot_device_heartbeats",
                columns: new[] { "iot_device_id", "time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_iot_devices_api_key_hash",
                table: "iot_devices",
                column: "api_key_hash");

            migrationBuilder.CreateIndex(
                name: "idx_iot_devices_device_code",
                table: "iot_devices",
                column: "device_code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_iot_devices_site",
                table: "iot_devices",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "idx_iot_devices_status_last_seen",
                table: "iot_devices",
                columns: new[] { "status", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_target_firmware_release_id",
                table: "iot_devices",
                column: "target_firmware_release_id");

            migrationBuilder.CreateIndex(
                name: "idx_iot_firmware_rollout",
                table: "iot_firmware_releases",
                columns: new[] { "hardware_revision", "is_published", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "idx_iot_firmware_version_hw",
                table: "iot_firmware_releases",
                columns: new[] { "version", "hardware_revision" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_iot_firmware_log_device_time",
                table: "iot_firmware_update_logs",
                columns: new[] { "iot_device_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_iot_firmware_log_status",
                table: "iot_firmware_update_logs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_iot_firmware_update_logs_iot_firmware_release_id",
                table: "iot_firmware_update_logs",
                column: "iot_firmware_release_id");

            // Sprint IoT-1 (#242) — convert iot_device_heartbeats sang TimescaleDB hypertable.
            migrationBuilder.Sql("SELECT create_hypertable('iot_device_heartbeats', 'time', if_not_exists => TRUE, migrate_data => TRUE);");

            // Retention policy: heartbeat lưu 30 ngày (phù hợp cho audit + offline triage).
            migrationBuilder.Sql("SELECT add_retention_policy('iot_device_heartbeats', INTERVAL '30 days', if_not_exists => TRUE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iot_device_calibrations");

            migrationBuilder.DropTable(
                name: "iot_device_heartbeats");

            migrationBuilder.DropTable(
                name: "iot_firmware_update_logs");

            migrationBuilder.DropTable(
                name: "iot_devices");

            migrationBuilder.DropTable(
                name: "iot_firmware_releases");
        }
    }
}
