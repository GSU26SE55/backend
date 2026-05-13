using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountInvitation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "invitation_expired_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invitation_token",
                table: "accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_invitation_token",
                table: "accounts",
                column: "invitation_token",
                filter: "\"invitation_token\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_invitation_token",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "invitation_expired_at",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "invitation_token",
                table: "accounts");
        }
    }
}
