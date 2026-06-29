using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillTicketParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill Owner participant cho ticket cũ (Customer tạo ticket trước khi có participant model).
            // Idempotent: bỏ qua ticket đã có active Owner row.
            migrationBuilder.Sql(@"
                INSERT INTO ticket_participants (
                    id, ticket_id, user_id, user_role, participant_type, can_post, can_view_internal,
                    added_by_user_id, added_at, created_at, is_deleted
                )
                SELECT gen_random_uuid(), t.id, t.customer_id, 4, 1, true, false,
                       t.customer_id, t.created_at, NOW(), false
                FROM tickets t
                WHERE t.is_deleted = false
                AND NOT EXISTS (
                    SELECT 1 FROM ticket_participants p
                    WHERE p.ticket_id = t.id AND p.user_id = t.customer_id
                      AND p.removed_at IS NULL AND p.is_deleted = false
                );
            ");

            // Backfill PrimaryAssignee participant cho ticket cũ đã có Staff được gán.
            // Idempotent: bỏ qua ticket đã có active PrimaryAssignee row.
            migrationBuilder.Sql(@"
                INSERT INTO ticket_participants (
                    id, ticket_id, user_id, user_role, participant_type, can_post, can_view_internal,
                    added_by_user_id, added_at, created_at, is_deleted
                )
                SELECT gen_random_uuid(), t.id, t.assigned_staff_id, 3, 2, true, true,
                       t.assigned_staff_id, COALESCE(t.updated_at, t.created_at), NOW(), false
                FROM tickets t
                WHERE t.is_deleted = false AND t.assigned_staff_id IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1 FROM ticket_participants p
                    WHERE p.ticket_id = t.id AND p.user_id = t.assigned_staff_id
                      AND p.participant_type = 2 AND p.removed_at IS NULL AND p.is_deleted = false
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op có chủ đích: đây là data backfill cho ticket cũ, không có cách an toàn để phân biệt
            // row được backfill với row tạo qua flow thường (cùng AddedByUserId = chính UserId) mà không
            // xóa nhầm dữ liệu hợp lệ. Rollback migration này chỉ revert về trạng thái "không backfill thêm",
            // không xóa participant đã tồn tại.
        }
    }
}
