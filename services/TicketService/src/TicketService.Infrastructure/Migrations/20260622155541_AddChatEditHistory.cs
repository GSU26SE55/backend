using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatEditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "edit_count",
                table: "ticket_chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "edited_at",
                table: "ticket_chats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_edited_by_user_id",
                table: "ticket_chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_chat_edits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_body = table.Column<string>(type: "text", nullable: false),
                    new_body = table.Column<string>(type: "text", nullable: false),
                    edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edited_by_role = table.Column<int>(type: "integer", nullable: false),
                    edit_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_chat_edits", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_chat_edits_ticket_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "ticket_chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_chat_edits_chat_id",
                table: "ticket_chat_edits",
                column: "chat_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_chat_edits");

            migrationBuilder.DropColumn(
                name: "edit_count",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "last_edited_by_user_id",
                table: "ticket_chats");
        }
    }
}
