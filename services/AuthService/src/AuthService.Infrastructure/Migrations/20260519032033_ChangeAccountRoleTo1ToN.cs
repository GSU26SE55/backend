using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <summary>
    /// Đổi quan hệ Role ↔ Account từ N-N (join table account_roles) sang 1-N
    /// (mỗi Account chỉ có duy nhất 1 role qua FK accounts.role_id → roles.id).
    ///
    /// Up sequence (BẮT BUỘC theo thứ tự để không mất data):
    ///   1. Thêm columns role_id (nullable tạm), role_assigned_at, role_assigned_by vào accounts.
    ///   2. Backfill role_id từ account_roles — chọn role có assigned_at mới nhất nếu account có >1 active role.
    ///      Account không có active role nào → fallback Customer role (44444444-4444-4444-4444-444444444444).
    ///   3. ALTER role_id SET NOT NULL.
    ///   4. Add index + FK Restrict.
    ///   5. Drop bảng account_roles.
    ///
    /// Down: tạo lại account_roles, populate từ accounts.role_id, drop role_id columns.
    /// </summary>
    public partial class ChangeAccountRoleTo1ToN : Migration
    {
        private const string CustomerRoleIdLiteral = "'44444444-4444-4444-4444-444444444444'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add columns. role_id nullable trong lúc backfill.
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "role_assigned_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_assigned_by",
                table: "accounts",
                type: "uuid",
                nullable: true);

            // 2a) Backfill role_id từ active assignment mới nhất.
            migrationBuilder.Sql($@"
                UPDATE accounts a
                SET role_id = sub.role_id,
                    role_assigned_at = sub.assigned_at,
                    role_assigned_by = sub.assigned_by
                FROM (
                    SELECT DISTINCT ON (ar.account_id)
                           ar.account_id, ar.role_id, ar.assigned_at, ar.assigned_by
                    FROM account_roles ar
                    WHERE ar.is_deleted = false
                      AND ar.is_active = true
                      AND (ar.expired_at IS NULL OR ar.expired_at > NOW())
                    ORDER BY ar.account_id, ar.assigned_at DESC
                ) sub
                WHERE a.id = sub.account_id;
            ");

            // 2b) Fallback: account chưa có active role → gán Customer.
            migrationBuilder.Sql($@"
                UPDATE accounts
                SET role_id = {CustomerRoleIdLiteral}::uuid,
                    role_assigned_at = COALESCE(role_assigned_at, NOW())
                WHERE role_id IS NULL;
            ");

            // 3) Bắt buộc NOT NULL sau khi backfill xong.
            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "accounts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 4) Index + FK.
            migrationBuilder.CreateIndex(
                name: "IX_accounts_role_id",
                table: "accounts",
                column: "role_id");

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_roles_role_id",
                table: "accounts",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // 5) Drop bảng account_roles (data đã được migrate sang accounts.role_id).
            migrationBuilder.DropTable(
                name: "account_roles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) Tạo lại bảng account_roles trống.
            migrationBuilder.CreateTable(
                name: "account_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_roles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_account_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_roles_account_id",
                table: "account_roles",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_roles_account_id_role_id",
                table: "account_roles",
                columns: new[] { "account_id", "role_id" },
                unique: true,
                filter: "\"is_deleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_account_roles_is_active",
                table: "account_roles",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_account_roles_role_id",
                table: "account_roles",
                column: "role_id");

            // 2) Populate account_roles từ accounts.role_id để không mất role-mapping khi rollback.
            migrationBuilder.Sql(@"
                INSERT INTO account_roles (id, account_id, role_id, assigned_at, assigned_by, is_active, is_deleted, created_at)
                SELECT gen_random_uuid(), a.id, a.role_id, COALESCE(a.role_assigned_at, NOW()), a.role_assigned_by, true, false, NOW()
                FROM accounts a;
            ");

            // 3) Drop FK + index + columns role_id/role_assigned_*.
            migrationBuilder.DropForeignKey(
                name: "FK_accounts_roles_role_id",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_role_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "role_assigned_at",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "role_assigned_by",
                table: "accounts");
        }
    }
}
