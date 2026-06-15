using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GH-295 — 2FA Hardening.
    ///
    /// <b>⚠️ ROLLBACK WARNING:</b> Sau khi migration này lên production VÀ có user enroll 2FA mới
    /// (qua /2fa/confirm), <c>accounts.two_factor_secret</c> sẽ chứa ciphertext format
    /// <c>enc:v1:{base64}</c> dài ~200-300 chars (có thể cao hơn nếu DataProtection key rotation tạo
    /// nested envelopes). Nếu rollback Down(): cột bị shrink về <c>varchar(256)</c> →
    /// <b>PostgreSQL sẽ THROW</b> nếu có row vượt 256 chars (không silent truncate cho NOT NULL).
    /// Để rollback an toàn:
    ///   1. Xóa hoặc clear toàn bộ <c>two_factor_secret</c> dài hơn 256 chars TRƯỚC khi run Down.
    ///   2. SQL: <c>UPDATE accounts SET two_factor_secret=NULL, two_factor_secret_encrypted_at=NULL, two_factor_enabled=false WHERE LENGTH(two_factor_secret) &gt; 256;</c>
    ///   3. Sau đó chạy <c>dotnet ef database update {previous-migration}</c>.
    /// User bị clear sẽ phải re-enroll 2FA. Backup codes table sẽ bị Drop hoàn toàn.
    /// </remarks>
    public partial class Add2FAHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "two_factor_secret",
                table: "accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "two_factor_secret_encrypted_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "backup_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    redeemed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_backup_codes_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_codes_account_id_redeemed_at",
                table: "backup_codes",
                columns: new[] { "account_id", "redeemed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_backup_codes_is_deleted",
                table: "backup_codes",
                column: "is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_codes");

            migrationBuilder.DropColumn(
                name: "two_factor_secret_encrypted_at",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "two_factor_secret",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
