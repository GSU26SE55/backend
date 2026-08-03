using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <summary>
    /// GH-783 — dọn alert SohDegradation trùng lặp do dedup cũ sinh ra (window 1 giờ hết hạn là
    /// tạo alert mới dù alert cũ vẫn Open: 188 alert Open trên 9 asset ở môi trường E2E).
    ///
    /// Data-only, KHÔNG đổi schema. Với mỗi asset: giữ alert mới nhất theo detected_at, phần còn
    /// lại chuyển status = 3 (Merged) + merged_into_alert_id trỏ về alert giữ lại. Không xoá dữ liệu.
    ///
    /// Enum: anomaly_type 8 = SohDegradation · status 1 = Open, 2 = Acknowledged, 3 = Merged.
    /// </summary>
    public partial class MergeDuplicateOpenSohAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                WITH keep AS (
                    SELECT DISTINCT ON (battery_asset_id) id, battery_asset_id
                    FROM alerts
                    WHERE is_deleted = false
                      AND anomaly_type = 8
                      AND status IN (1, 2)
                      AND battery_asset_id IS NOT NULL
                    ORDER BY battery_asset_id, detected_at DESC
                )
                UPDATE alerts a
                SET status = 3,
                    merged_into_alert_id = k.id
                FROM keep k
                WHERE a.battery_asset_id = k.battery_asset_id
                  AND a.id <> k.id
                  AND a.is_deleted = false
                  AND a.anomaly_type = 8
                  AND a.status IN (1, 2);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Runtime mới không bao giờ insert alert SohDegradation ở status Merged (dedup là
            // update in-place), nên tập dưới đây đúng bằng tập mà Up() vừa đụng.
            // ⚠️ Alert vốn là Acknowledged (2) sẽ quay về Open (1) — không lưu được trạng thái gốc.
            // Chấp nhận được: đây là cleanup dữ liệu rác, không phải schema change có thể mất data.
            migrationBuilder.Sql(@"
                UPDATE alerts
                SET status = 1,
                    merged_into_alert_id = NULL
                WHERE is_deleted = false
                  AND anomaly_type = 8
                  AND status = 3
                  AND merged_into_alert_id IS NOT NULL;
            ");
        }
    }
}
