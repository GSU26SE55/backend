using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalIssuedAtToRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "original_issued_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            // #AUTH-28: backfill row cũ — coi IssuedAt làm OriginalIssuedAt (đúng cho token chưa rotate;
            // sai nhẹ cho token đã rotate vì IssuedAt là thời điểm rotation cuối, nhưng acceptable
            // vì runtime fallback ?? IssuedAt giữ behavior cũ cho row null).
            migrationBuilder.Sql(@"
                UPDATE refresh_tokens
                SET original_issued_at = issued_at
                WHERE original_issued_at IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_issued_at",
                table: "refresh_tokens");
        }
    }
}
