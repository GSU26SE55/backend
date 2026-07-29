using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations;

/// <summary>
/// FIX index ux_tickets_active_auto_per_asset_category — filter status SAI GIÁ TRỊ.
///
/// Migration gốc (20260611145402_AddAlertTicketSagaFoundation) viết:
///     status NOT IN (6, 7, 8, 9)
/// kèm comment "Resolved=6, ClosedPendingRate=7, Closed=8, ClosedRejected=9".
/// Nhưng TicketStatusEnum thực tế là:
///     WaitingParts=6, WaitingOnsiteSchedule=7, Resolved=8, Escalated=9,
///     ClosedPendingRate=10, Closed=11, ClosedRejected=12
///
/// Hệ quả (2 chiều, đều sai):
///   1. Ticket ĐÃ ĐÓNG (10/11/12) vẫn nằm trong index → chiếm slot VĨNH VIỄN cho
///      (battery_asset_id, category) → saga không bao giờ tạo được ticket auto mới
///      cho pin đó nữa (23505 duplicate key).
///   2. Ticket ĐANG XỬ LÝ WaitingParts(6)/WaitingOnsiteSchedule(7)/Escalated(9)
///      bị loại khỏi index → mất tác dụng chống trùng đúng lúc cần nhất.
///
/// Sửa: loại các trạng thái KẾT THÚC — Resolved(8), ClosedPendingRate(10),
/// Closed(11), ClosedRejected(12). Các trạng thái còn lại = đang mở → giữ trong index.
/// </summary>
[DbContext(typeof(TicketDbContext))]
[Migration("20260722140500_FixActiveAutoTicketIndexStatuses")]
public partial class FixActiveAutoTicketIndexStatuses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
        migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
ON tickets (battery_asset_id, category)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (8, 10, 11, 12);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_tickets_active_auto_per_asset_category;");
        migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ux_tickets_active_auto_per_asset_category
ON tickets (battery_asset_id, category)
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8, 9);");
    }
}
