using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// #AUTH-75: composite non-unique index trên (email, is_deleted) để query lookup
    /// "WHERE Email = X AND IsDeleted = false" hit cả 2 cột — pattern dùng nhiều ở
    /// Login/Register/ForgotPassword/Verify*.
    ///
    /// #AUTH-34 lưu ý: `xmin` là PostgreSQL SYSTEM COLUMN (auto-present mọi table) — KHÔNG cần
    /// AddColumn. EF model snapshot thấy shadow property và auto-generate AddColumn nhầm.
    /// Migration này skip AddColumn xmin (system column tự có) — chỉ create index thật.
    /// </summary>
    public partial class AddAccountEmailIsDeletedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KHÔNG AddColumn xmin — đã có sẵn ở PG system catalog.

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email_isdeleted",
                table: "accounts",
                columns: new[] { "email", "is_deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_email_isdeleted",
                table: "accounts");

            // KHÔNG DropColumn xmin — system column không xoá được.
        }
    }
}
