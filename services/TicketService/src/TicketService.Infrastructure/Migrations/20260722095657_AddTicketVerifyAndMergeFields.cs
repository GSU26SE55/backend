using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketVerifyAndMergeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_verify_reason",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ai_verify_score",
                table: "tickets",
                type: "double precision",
                nullable: true);

            // TicketVerifyStatusEnum.Pending = 1 (enum bắt đầu từ 1). Ticket cũ = Pending.
            migrationBuilder.AddColumn<int>(
                name: "ai_verify_status",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "battery_serial_number",
                table: "tickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "detected_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "duplicate_reason",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suspected_duplicate_of_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_verify_reason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ai_verify_score",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ai_verify_status",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "battery_serial_number",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "detected_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "duplicate_reason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "merged_into_ticket_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "suspected_duplicate_of_ticket_id",
                table: "tickets");
        }
    }
}
