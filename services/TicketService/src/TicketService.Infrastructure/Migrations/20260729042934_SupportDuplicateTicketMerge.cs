using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupportDuplicateTicketMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "close_reason",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_ticket_id",
                table: "ticket_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_ticket_id",
                table: "ticket_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_merged_into_ticket_id",
                table: "tickets",
                column: "merged_into_ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_source_ticket_id",
                table: "ticket_attachments",
                column: "source_ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_activities_source_ticket_id",
                table: "ticket_activities",
                column: "source_ticket_id");

            // Data conversion is intentionally one-way: restoring 14 in Down() would corrupt
            // legitimate Open tickets created after this migration.
            migrationBuilder.Sql("UPDATE tickets SET status = 2 WHERE status = 14;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_merged_into_ticket_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_ticket_attachments_source_ticket_id",
                table: "ticket_attachments");

            migrationBuilder.DropIndex(
                name: "IX_ticket_activities_source_ticket_id",
                table: "ticket_activities");

            migrationBuilder.DropColumn(
                name: "close_reason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "source_ticket_id",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "source_ticket_id",
                table: "ticket_activities");

        }
    }
}
