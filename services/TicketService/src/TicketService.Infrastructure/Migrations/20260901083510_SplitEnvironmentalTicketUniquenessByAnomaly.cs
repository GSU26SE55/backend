using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitEnvironmentalTicketUniquenessByAnomaly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "anomaly_type",
                table: "tickets",
                type: "integer",
                nullable: true);

            // Backfill: ticket auto đang mở lấy lại loại bất thường từ alert gốc.
            // KHÔNG join được (alerts nằm ở battery_db) nên suy từ title — ba loại môi trường là
            // nhóm DUY NHẤT cần phân biệt để index mới hoạt động; ticket pin không dùng cột này.
            migrationBuilder.Sql(@"
UPDATE tickets SET anomaly_type = 9  WHERE is_deleted = false AND site_id IS NOT NULL AND title ILIKE '%Ambient Temperature%';
UPDATE tickets SET anomaly_type = 10 WHERE is_deleted = false AND site_id IS NOT NULL AND title ILIKE '%High Humidity%';
UPDATE tickets SET anomaly_type = 11 WHERE is_deleted = false AND site_id IS NOT NULL AND title ILIKE '%Temperature + Humidity%';
UPDATE tickets SET anomaly_type = 18 WHERE is_deleted = false AND site_id IS NOT NULL AND title ILIKE '%Gas Concentration%';
UPDATE tickets SET anomaly_type = 19 WHERE is_deleted = false AND site_id IS NOT NULL AND title ILIKE '%Water Leak%';");

            // Một index đang gánh hai loại ticket có bản chất khác nhau, nên khoá của nó phải là
            // mẫu số chung nhỏ nhất: (battery_asset_id, category). Ticket môi trường cấp site đều
            // mang `battery_asset_id = Guid.Empty` + `category = Repair` ⇒ toàn hệ thống chỉ được
            // MỘT ticket môi trường đang mở, không phân biệt loại sự cố lẫn site. Gas nổ trước thì
            // nước và nhiệt độ sau đó bị gắn vào ticket gas.
            //
            // Tách làm hai partial index loại trừ nhau theo `site_id`:
            //   - ticket pin  (site_id IS NULL)     → giữ nguyên khoá cũ
            //   - ticket site (site_id IS NOT NULL) → khoá theo (site, loại sự cố)
            //
            // Vì sao KHÔNG gộp thành một index (battery_asset_id, site_id, category, anomaly_type):
            // ticket pin có `site_id` NULL, mà trong unique index Postgres coi NULL <> NULL, nên
            // ràng buộc per-asset sẽ mất tác dụng ÂM THẦM — mỗi viên pin lại đẻ được nhiều ticket
            // cùng category. Hai index loại trừ nhau thì không có NULL nào tham gia khoá.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
ON tickets (battery_asset_id, category)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8)
  AND site_id IS NULL;");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_env_per_site_anomaly
ON tickets (site_id, anomaly_type)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8)
  AND site_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_env_per_site_anomaly;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
ON tickets (battery_asset_id, category)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8);");

            migrationBuilder.DropColumn(
                name: "anomaly_type",
                table: "tickets");
        }
    }
}
