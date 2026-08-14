using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFilteredUniqueIndexChatTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_chat_translations_chat_lang",
                table: "ticket_chat_translations");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_translations_chat_lang",
                table: "ticket_chat_translations",
                columns: new[] { "chat_id", "target_language" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_chat_translations_chat_lang",
                table: "ticket_chat_translations");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_translations_chat_lang",
                table: "ticket_chat_translations",
                columns: new[] { "chat_id", "target_language" },
                unique: true);
        }
    }
}
