using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_chat_mentions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentioned_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentioned_user_role = table.Column<int>(type: "integer", nullable: false),
                    mentioned_display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_chat_mentions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_chat_mentions_ticket_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "ticket_chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_chat_mentions_chat_id",
                table: "ticket_chat_mentions",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_mentions_user_unread",
                table: "ticket_chat_mentions",
                columns: new[] { "mentioned_user_id", "is_acknowledged" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_chat_mentions");
        }
    }
}
