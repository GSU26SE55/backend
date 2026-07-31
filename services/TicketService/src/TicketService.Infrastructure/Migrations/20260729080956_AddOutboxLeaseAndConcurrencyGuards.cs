using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxLeaseAndConcurrencyGuards : Migration
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
                name: "ux_ticket_participants_active_user",
                table: "ticket_participants",
                columns: new[] { "ticket_id", "user_id" },
                unique: true,
                filter: "removed_at IS NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_ticket_assignments_active_primary",
                table: "ticket_assignments",
                column: "ticket_id",
                unique: true,
                filter: "role = 1 AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_claimable",
                table: "outbox_messages",
                columns: new[] { "processed_at_utc", "lease_until_utc", "occurred_at_utc" },
                filter: "processed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ticket_participants_active_user",
                table: "ticket_participants");

            migrationBuilder.DropIndex(
                name: "ux_ticket_assignments_active_primary",
                table: "ticket_assignments");

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
