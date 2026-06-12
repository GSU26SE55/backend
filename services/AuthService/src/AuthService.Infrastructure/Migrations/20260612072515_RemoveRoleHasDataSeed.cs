using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoleHasDataSeed : Migration
    {
        private const string LegacyAdminRoleId = "11111111-1111-1111-1111-111111111111";
        private const string LegacyManagerRoleId = "22222222-2222-2222-2222-222222222222";
        private const string LegacyStaffRoleId = "33333333-3333-3333-3333-333333333333";
        private const string LegacyCustomerRoleId = "44444444-4444-4444-4444-444444444444";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // System roles giờ được seed runtime bằng Guid.NewGuid() qua AuthDataSeeder.
            // Cần dọn mọi FK trỏ tới legacy hardcoded role Ids trước khi xoá row roles.
            // Lệnh idempotent: chạy lại nhiều lần vẫn an toàn.

            // 1) Xoá role_permissions referencing legacy roles.
            migrationBuilder.Sql($@"
                DELETE FROM role_permissions
                WHERE role_id IN (
                    '{LegacyAdminRoleId}'::uuid,
                    '{LegacyManagerRoleId}'::uuid,
                    '{LegacyStaffRoleId}'::uuid,
                    '{LegacyCustomerRoleId}'::uuid
                );
            ");

            // 2) Xoá accounts gắn legacy roles (sẽ được AuthDataSeeder seed lại admin sau migrate).
            //    Cascade thủ công các bảng phụ thuộc Account để tránh FK violation.
            migrationBuilder.Sql($@"
                WITH legacy_accounts AS (
                    SELECT id FROM accounts
                    WHERE role_id IN (
                        '{LegacyAdminRoleId}'::uuid,
                        '{LegacyManagerRoleId}'::uuid,
                        '{LegacyStaffRoleId}'::uuid,
                        '{LegacyCustomerRoleId}'::uuid
                    )
                )
                DELETE FROM refresh_tokens WHERE account_id IN (SELECT id FROM legacy_accounts);
            ");

            migrationBuilder.Sql($@"
                WITH legacy_accounts AS (
                    SELECT id FROM accounts
                    WHERE role_id IN (
                        '{LegacyAdminRoleId}'::uuid,
                        '{LegacyManagerRoleId}'::uuid,
                        '{LegacyStaffRoleId}'::uuid,
                        '{LegacyCustomerRoleId}'::uuid
                    )
                )
                DELETE FROM login_attempts WHERE account_id IN (SELECT id FROM legacy_accounts);
            ");

            migrationBuilder.Sql($@"
                WITH legacy_accounts AS (
                    SELECT id FROM accounts
                    WHERE role_id IN (
                        '{LegacyAdminRoleId}'::uuid,
                        '{LegacyManagerRoleId}'::uuid,
                        '{LegacyStaffRoleId}'::uuid,
                        '{LegacyCustomerRoleId}'::uuid
                    )
                )
                DELETE FROM account_profiles WHERE account_id IN (SELECT id FROM legacy_accounts);
            ");

            migrationBuilder.Sql($@"
                WITH legacy_accounts AS (
                    SELECT id FROM accounts
                    WHERE role_id IN (
                        '{LegacyAdminRoleId}'::uuid,
                        '{LegacyManagerRoleId}'::uuid,
                        '{LegacyStaffRoleId}'::uuid,
                        '{LegacyCustomerRoleId}'::uuid
                    )
                )
                DELETE FROM staff_profiles WHERE account_id IN (SELECT id FROM legacy_accounts);
            ");

            migrationBuilder.Sql($@"
                DELETE FROM accounts
                WHERE role_id IN (
                    '{LegacyAdminRoleId}'::uuid,
                    '{LegacyManagerRoleId}'::uuid,
                    '{LegacyStaffRoleId}'::uuid,
                    '{LegacyCustomerRoleId}'::uuid
                );
            ");

            // 3) Cuối cùng xoá legacy roles.
            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid(LegacyAdminRoleId));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid(LegacyManagerRoleId));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid(LegacyStaffRoleId));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid(LegacyCustomerRoleId));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "created_at", "created_by", "deleted_at", "description", "is_system_role", "name", "normalized_name", "status", "updated_at" },
                values: new object[,]
                {
                    { new Guid(LegacyAdminRoleId), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Quản trị viên hệ thống, có toàn quyền.", true, "Admin", "ADMIN", 1, null },
                    { new Guid(LegacyManagerRoleId), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Quản lý vận hành, điều phối kỹ thuật viên và đơn bảo trì.", true, "Manager", "MANAGER", 1, null },
                    { new Guid(LegacyStaffRoleId), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Nhân viên vận hành hệ thống.", true, "Staff", "STAFF", 1, null },
                    { new Guid(LegacyCustomerRoleId), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Khách hàng sử dụng dịch vụ bảo trì pin năng lượng mặt trời.", true, "Customer", "CUSTOMER", 1, null }
                });
        }
    }
}
