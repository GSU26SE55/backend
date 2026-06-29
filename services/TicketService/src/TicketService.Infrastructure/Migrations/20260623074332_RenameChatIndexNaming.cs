using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameChatIndexNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_ticket_chat_mentions_chat_id",
                table: "ticket_chat_mentions",
                newName: "ix_ticket_chat_mentions_chat_id");

            migrationBuilder.RenameIndex(
                name: "IX_chat_templates_team_id",
                table: "chat_templates",
                newName: "ix_chat_templates_team_id");

            migrationBuilder.RenameIndex(
                name: "IX_chat_templates_scope",
                table: "chat_templates",
                newName: "ix_chat_templates_scope");

            migrationBuilder.RenameIndex(
                name: "IX_chat_ai_suggestions_ticket_id",
                table: "chat_ai_suggestions",
                newName: "ix_chat_ai_suggestions_ticket_id");

            migrationBuilder.RenameIndex(
                name: "IX_chat_ai_suggestions_final_chat_id",
                table: "chat_ai_suggestions",
                newName: "ix_chat_ai_suggestions_final_chat_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_ticket_chat_mentions_chat_id",
                table: "ticket_chat_mentions",
                newName: "IX_ticket_chat_mentions_chat_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_templates_team_id",
                table: "chat_templates",
                newName: "IX_chat_templates_team_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_templates_scope",
                table: "chat_templates",
                newName: "IX_chat_templates_scope");

            migrationBuilder.RenameIndex(
                name: "ix_chat_ai_suggestions_ticket_id",
                table: "chat_ai_suggestions",
                newName: "IX_chat_ai_suggestions_ticket_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_ai_suggestions_final_chat_id",
                table: "chat_ai_suggestions",
                newName: "IX_chat_ai_suggestions_final_chat_id");
        }
    }
}
