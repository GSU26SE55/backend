using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAiSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_ai_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    intent = table.Column<int>(type: "integer", nullable: false),
                    suggestions = table.Column<string>(type: "jsonb", nullable: false),
                    selected_index = table.Column<int>(type: "integer", nullable: true),
                    edited_before_post = table.Column<bool>(type: "boolean", nullable: false),
                    final_chat_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_ai_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_ai_suggestions_ticket_chats_final_chat_id",
                        column: x => x.final_chat_id,
                        principalTable: "ticket_chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chat_ai_suggestions_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_ai_suggestions_final_chat_id",
                table: "chat_ai_suggestions",
                column: "final_chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_ai_suggestions_ticket_id",
                table: "chat_ai_suggestions",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_ai_suggestions");
        }
    }
}
