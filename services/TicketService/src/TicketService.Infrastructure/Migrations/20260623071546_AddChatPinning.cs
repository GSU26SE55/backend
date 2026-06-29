using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_pinned",
                table: "ticket_chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "pinned_at",
                table: "ticket_chats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pinned_by_user_id",
                table: "ticket_chats",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_pinned",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "pinned_by_user_id",
                table: "ticket_chats");
        }
    }
}
