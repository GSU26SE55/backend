using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetLevelMaintenanceSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "maintenance_interval_months",
                table: "battery_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_maintenance_at_utc",
                table: "battery_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maintenance_cycle_no",
                table: "battery_assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_maintenance_due_at_utc",
                table: "battery_assets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "maintenance_cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_no = table.Column<int>(type: "integer", nullable: false),
                    due_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    soh_percent_at_completion = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    outcome_summary = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_cycles", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_cycles_battery_assets_battery_asset_id",
                        column: x => x.battery_asset_id,
                        principalTable: "battery_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Backfill ────────────────────────────────────────────────────────────
            //
            // next_maintenance_due_at_utc là NOT NULL nên EF đặt mặc định 0001-01-01 cho mọi
            // dòng sẵn có. Để nguyên thì tick đầu tiên của worker sẽ coi TOÀN BỘ pin là quá
            // hạn và mở ticket hàng loạt — kèm theo đó là một trận bão thông báo.
            //
            // Mốc kỳ kế tiếp lấy theo thứ tự ưu tiên:
            //   1. Lần bảo trì gần nhất suy từ install_date + chu kỳ, nếu mốc đó còn ở tương lai
            //   2. Còn lại (pin quá hạn hoặc lắp đã lâu) → giãn ra tương lai, rải đều theo
            //      lead time để không dồn vào một ngày
            //
            // Chu kỳ dùng maintenance_interval_months của loại pin, thiếu thì mặc định 6 tháng.
            migrationBuilder.Sql("""
                WITH cycle AS (
                    SELECT a.id,
                           a.install_date,
                           COALESCE(t.maintenance_interval_months, 6) AS months,
                           row_number() OVER (ORDER BY a.install_date, a.id) AS seq
                    FROM battery_assets a
                    JOIN battery_types t ON t.id = a.battery_type_id
                )
                UPDATE battery_assets a
                SET next_maintenance_due_at_utc = CASE
                        WHEN c.install_date + (c.months || ' months')::interval > now()
                            THEN c.install_date + (c.months || ' months')::interval
                        ELSE now() + interval '7 days' + ((c.seq - 1) % 7) * interval '1 day'
                    END
                FROM cycle c
                WHERE a.id = c.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_battery_assets_next_maintenance_due",
                table: "battery_assets",
                column: "next_maintenance_due_at_utc",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_cycles_asset_due",
                table: "maintenance_cycles",
                columns: new[] { "battery_asset_id", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_cycles_asset_cycle_no",
                table: "maintenance_cycles",
                columns: new[] { "battery_asset_id", "cycle_no" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_cycles");

            migrationBuilder.DropIndex(
                name: "ix_battery_assets_next_maintenance_due",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "maintenance_interval_months",
                table: "battery_types");

            migrationBuilder.DropColumn(
                name: "last_maintenance_at_utc",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "maintenance_cycle_no",
                table: "battery_assets");

            migrationBuilder.DropColumn(
                name: "next_maintenance_due_at_utc",
                table: "battery_assets");
        }
    }
}
