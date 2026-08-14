using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxLeaseClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_until_utc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_claimable",
                table: "outbox_messages",
                columns: new[] { "processed_at", "lease_until_utc", "occurred_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_outbox_claimable",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lease_until_utc",
                table: "outbox_messages");
        }
    }
}
