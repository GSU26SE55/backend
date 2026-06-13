using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 5B #241 — seed 2 permission `ticket.saga.view` + `ticket.saga.reprocess`
    /// vào bảng <c>permissions</c> và bind cho Admin (cả 2) + Manager (`view` only).
    ///
    /// Idempotent: dùng <c>ON CONFLICT DO NOTHING</c> để safe khi seed chạy nhiều lần.
    /// IDs sinh deterministic để rollback chính xác.
    /// </summary>
    public partial class SeedSagaPermissions : Migration
    {
        private const string TicketSagaViewId = "a1c91e29-4d8b-4e90-9c43-f43c6d1c5b01";
        private const string TicketSagaReprocessId = "a1c91e29-4d8b-4e90-9c43-f43c6d1c5b02";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Insert 2 new permissions.
            migrationBuilder.Sql($@"
INSERT INTO permissions (id, code, module, description, is_system_permission, created_at, is_deleted)
VALUES
  ('{TicketSagaViewId}'::uuid, 'ticket.saga.view', 'TicketSaga',
   'Xem danh sách Alert-Ticket Saga + state hiện tại', true,
   timezone('utc', now()), false),
  ('{TicketSagaReprocessId}'::uuid, 'ticket.saga.reprocess', 'TicketSaga',
   'Reprocess Saga đang Failed (admin only)', true,
   timezone('utc', now()), false)
ON CONFLICT (code) DO NOTHING;");

            // 2. Bind to Admin role (BOTH permissions).
            migrationBuilder.Sql($@"
INSERT INTO role_permissions (id, role_id, permission_id, assigned_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, timezone('utc', now()), timezone('utc', now())
FROM roles r
CROSS JOIN permissions p
WHERE r.normalized_name = 'ADMIN'
  AND p.code IN ('ticket.saga.view', 'ticket.saga.reprocess')
  AND NOT EXISTS (
    SELECT 1 FROM role_permissions rp
    WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );");

            // 3. Bind to Manager role (view ONLY).
            migrationBuilder.Sql($@"
INSERT INTO role_permissions (id, role_id, permission_id, assigned_at, created_at)
SELECT gen_random_uuid(), r.id, p.id, timezone('utc', now()), timezone('utc', now())
FROM roles r
CROSS JOIN permissions p
WHERE r.normalized_name = 'MANAGER'
  AND p.code = 'ticket.saga.view'
  AND NOT EXISTS (
    SELECT 1 FROM role_permissions rp
    WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse bindings first, then permissions (FK).
            migrationBuilder.Sql(@"
DELETE FROM role_permissions
WHERE permission_id IN (
  SELECT id FROM permissions
  WHERE code IN ('ticket.saga.view', 'ticket.saga.reprocess')
);");

            migrationBuilder.Sql(@"
DELETE FROM permissions
WHERE code IN ('ticket.saga.view', 'ticket.saga.reprocess');");
        }
    }
}
