using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <summary>
    /// Vá các database bị lệch cột retry của bảng <c>notifications</c>.
    ///
    /// <para><b>Vì sao là migration RIÊNG chứ không sửa thẳng
    /// <c>20260729161154_AddNotificationDispatchRetryColumns</c>:</b> migration đó đã merge vào
    /// <c>dev</c> và đã chạy trên các database hiện có, nên tên nó đã nằm trong
    /// <c>__EFMigrationsHistory</c>. Sửa nội dung nó KHÔNG bao giờ chạy lại — chỉ database dựng mới
    /// mới thấy bản sửa. Đúng quy trình (<c>.claude/rules/tech/be.md</c> §14) là để migration cũ
    /// nguyên vẹn và bù bằng một migration mới.</para>
    ///
    /// <para><b>Vá cái gì:</b> một nhánh phát triển cũ từng đặt tên cột là <c>attempt_count</c>
    /// (kèm <c>next_attempt_at</c>) trước khi chốt tên <c>dispatch_attempt_count</c>. Database nào
    /// đã chạy nhánh đó sẽ thừa cột <c>attempt_count</c> và thừa index
    /// <c>IX_notifications_status_next_attempt_at</c>. Migration này gộp dữ liệu retry về cột đúng
    /// rồi dọn phần thừa.</para>
    ///
    /// <para><b>An toàn:</b> mọi lệnh đều có điều kiện tồn tại, nên trên database khoẻ mạnh (đi đúng
    /// đường migration của repo này) toàn bộ <c>Up()</c> là no-op.</para>
    ///
    /// <para><b>Giới hạn đã biết:</b> nếu một database vừa có sẵn <c>next_attempt_at</c> vừa CHƯA
    /// chạy <c>20260729161154</c> thì chính migration đó hỏng trước khi tới đây ("column already
    /// exists"). Trạng thái này không thể phát sinh từ repo này (nhánh cũ chưa từng được push).
    /// Nếu gặp, chạy tay đúng một lần rồi <c>dotnet ef database update</c> lại:
    /// <code>
    /// ALTER TABLE notifications DROP COLUMN IF EXISTS next_attempt_at;
    /// ALTER TABLE notifications RENAME COLUMN attempt_count TO dispatch_attempt_count;
    /// DROP INDEX IF EXISTS "IX_notifications_status_next_attempt_at";
    /// </code>
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class RepairLegacyNotificationRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    -- Bảng chưa tồn tại thì không có gì để vá. Không xảy ra trong thực tế vì
                    -- migration này chạy sau migration tạo bảng, nhưng giữ cho khối tự đứng vững.
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'public' AND table_name = 'notifications'
                    ) THEN
                        RETURN;
                    END IF;

                    -- 1) Bảo đảm hai cột đúng tên luôn tồn tại trước khi gộp dữ liệu.
                    --    Trên database khoẻ mạnh cả hai đã do 20260729161154 tạo nên bỏ qua hết.
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'dispatch_attempt_count'
                    ) THEN
                        ALTER TABLE notifications
                            ADD COLUMN dispatch_attempt_count integer NOT NULL DEFAULT 0;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'next_attempt_at'
                    ) THEN
                        ALTER TABLE notifications
                            ADD COLUMN next_attempt_at timestamp with time zone;
                    END IF;

                    -- 2) Gộp số lần thử từ cột cũ rồi bỏ nó đi. GREATEST giữ giá trị lớn hơn để
                    --    một dòng đã thử 3 lần không bị đặt ngược về 0 rồi gửi lại từ đầu.
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'notifications'
                          AND column_name = 'attempt_count'
                    ) THEN
                        UPDATE notifications
                        SET dispatch_attempt_count = GREATEST(
                                COALESCE(dispatch_attempt_count, 0),
                                COALESCE(attempt_count, 0));

                        ALTER TABLE notifications DROP COLUMN attempt_count;
                    END IF;
                END
                $migration$;
                """);

            // Index thừa do nhánh cũ sinh ra (EF đặt tên PascalCase mặc định). Repo này đặt tên
            // snake_case nên nó chắc chắn là rác, không phải index của migration nào đang sống.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_notifications_status_next_attempt_at";
                """);

            // Index thật của hàng đợi dispatch. Trên database khoẻ mạnh 20260729161154 đã tạo nên
            // IF NOT EXISTS làm câu này thành no-op; chỉ database lai mới thực sự tạo ở đây.
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_notifications_dispatch_queue
                    ON notifications (status, next_attempt_at, created_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cố ý để trống. Đây là migration DỌN DẸP: trên database khoẻ mạnh nó không tạo ra cấu
            // trúc mới nào của riêng mình. Cột và index mà nó chạm tới đều thuộc quyền sở hữu của
            // 20260729161154 — rollback chúng ở đây sẽ phá trạng thái migration kia đang giữ.
            //
            // Rollback đúng nghĩa của "đã dọn cột attempt_count thừa" là dựng lại một cột rác, nên
            // không làm. Chuỗi `database update <prev>` → `database update` vẫn chạy sạch.
        }
    }
}
