using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GH1176ReviseTicketStatusSlaWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "active_incident_episode_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_context",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_reason",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schedule_version",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_start_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            // Index ux_tickets_active_auto_per_asset_category liệt kê các trạng thái ĐÃ ĐÓNG theo
            // BẢNG MÃ CŨ (8, 10, 11, 12) để loại chúng khỏi ràng buộc "mỗi (pin, category) chỉ một
            // ticket auto đang hoạt động". Khối remap bên dưới đánh số lại toàn bộ status, nên nếu
            // giữ nguyên index thì cùng một con số mang ý nghĩa khác: ví dụ hai ticket đã đóng ở
            // status 11 cũ trở thành status 7 (Closed mới) — vẫn là đã đóng, nhưng 7 không nằm
            // trong danh sách loại trừ cũ nên cả hai lọt vào index và va nhau bằng lỗi 23505.
            //
            // Mỗi câu UPDATE là một statement riêng và unique index được kiểm tra sau TỪNG câu, nên
            // không thể trông chờ vào việc "cuối cùng thì dữ liệu vẫn hợp lệ": chỉ cần một bước
            // trung gian vi phạm là cả migration vỡ và service không khởi động nổi.
            //
            // Vì vậy: bỏ index trước khi remap, dựng lại sau khi remap theo BẢNG MÃ MỚI. Danh sách
            // loại trừ (6, 7, 8) = Completed/Closed/ClosedRejected, khớp đúng ActiveStatuses trong
            // CreateTicketFromAlertConsumer (Open, Pending, InProgress, Request, ReAssign).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");

            migrationBuilder.Sql(
                """
                UPDATE tickets
                SET status = 2,
                    pending_context = 2,
                    pending_reason = CASE WHEN status = 5 THEN 1 ELSE 2 END
                WHERE status IN (5, 6, 7);

                UPDATE tickets SET status = 6 WHERE status = 8;
                UPDATE tickets SET status = 5 WHERE status = 9;
                UPDATE tickets SET status = 7 WHERE status = 10;
                UPDATE tickets SET status = 7 WHERE status = 11;
                UPDATE tickets SET status = 8 WHERE status = 12;

                UPDATE sla_pause_events
                SET reason = CASE
                    WHEN reason = 3 THEN 2
                    WHEN reason = 4 THEN 1
                    ELSE reason
                END
                WHERE reason IN (3, 4);

                UPDATE tickets
                SET status = 5,
                    priority = 4,
                    is_incident = TRUE,
                    active_incident_episode_id = COALESCE(active_incident_episode_id, gen_random_uuid())
                WHERE status = 13;

                UPDATE sla_timers AS timer
                SET status = 5,
                    current_pause_started_at = NULL
                FROM tickets AS ticket
                WHERE timer.ticket_id = ticket.id
                  AND ticket.priority = 4
                  AND timer.status IN (1, 2);

                UPDATE sla_timers AS timer
                SET status = 2,
                    current_pause_started_at = COALESCE(timer.current_pause_started_at, NOW())
                FROM tickets AS ticket
                WHERE timer.ticket_id = ticket.id
                  AND ticket.status <> 3
                  AND ticket.priority <> 4
                  AND timer.status = 1;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
                ON tickets (battery_asset_id, category)
                WHERE origin_alert_id IS NOT NULL
                  AND is_deleted = false
                  AND status NOT IN (6, 7, 8);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_due_activation",
                table: "tickets",
                columns: new[] { "status", "scheduled_start_at_utc" },
                filter: "is_deleted = false AND status = 2 AND scheduled_start_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_due_activation",
                table: "tickets");

            // Trả index về đúng predicate trước khi Up chạy. Lưu ý Up KHÔNG remap ngược status,
            // nên sau khi rollback bảng mã vẫn là bảng mã mới còn index quay lại bảng mã cũ —
            // migration này vốn một chiều về dữ liệu, Down chỉ khôi phục schema.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
                ON tickets (battery_asset_id, category)
                WHERE origin_alert_id IS NOT NULL
                  AND is_deleted = false
                  AND status NOT IN (8, 10, 11, 12);
                """);

            migrationBuilder.DropColumn(
                name: "active_incident_episode_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "pending_context",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "pending_reason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "schedule_version",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "scheduled_start_at_utc",
                table: "tickets");
        }
    }
}
