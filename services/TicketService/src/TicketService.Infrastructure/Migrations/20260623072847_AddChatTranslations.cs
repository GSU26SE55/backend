using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_language",
                table: "ticket_chats",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_chat_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    translated_body = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    translated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_chat_translations", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_chat_translations_ticket_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "ticket_chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_translations_chat_lang",
                table: "ticket_chat_translations",
                columns: new[] { "chat_id", "target_language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_chat_translations");

            migrationBuilder.DropColumn(
                name: "original_language",
                table: "ticket_chats");
        }
    }
}
