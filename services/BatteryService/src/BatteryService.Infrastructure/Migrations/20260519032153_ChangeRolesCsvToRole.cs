using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <summary>
    /// Đổi <c>customer_accounts.roles_csv</c> (CSV multi-role) → <c>customer_accounts.role</c> (single role)
    /// để khớp với refactor 1-N ở AuthService — mỗi account chỉ có duy nhất 1 role.
    ///
    /// Up: rename + truncate giá trị CSV đầu tiên (split bằng dấu phẩy).
    /// Down: rename ngược lại — value cũ giữ nguyên (single role vẫn là CSV hợp lệ).
    /// </summary>
    public partial class ChangeRolesCsvToRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Tạo column mới (tạm thời nullable để backfill).
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "customer_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // 2) Backfill: lấy phần tử đầu tiên của CSV; fallback "Customer" nếu rỗng.
            migrationBuilder.Sql(@"
                UPDATE customer_accounts
                SET role = COALESCE(NULLIF(TRIM(split_part(roles_csv, ',', 1)), ''), 'Customer');
            ");

            // 3) Bắt buộc NOT NULL.
            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "customer_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // 4) Drop column cũ.
            migrationBuilder.DropColumn(
                name: "roles_csv",
                table: "customer_accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "roles_csv",
                table: "customer_accounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Backfill ngược: single role làm CSV (1 phần tử).
            migrationBuilder.Sql("UPDATE customer_accounts SET roles_csv = role;");

            migrationBuilder.AlterColumn<string>(
                name: "roles_csv",
                table: "customer_accounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "role",
                table: "customer_accounts");
        }
    }
}
