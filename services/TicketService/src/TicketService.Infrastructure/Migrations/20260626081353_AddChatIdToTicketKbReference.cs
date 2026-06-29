using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatIdToTicketKbReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ticket_kb_references_ticket_id_kb_article_id_reference_type\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"ix_ticket_kb_references_ticket_id_kb_article_id_reference_type\";");

            migrationBuilder.AddColumn<Guid>(
                name: "chat_id",
                table: "ticket_kb_references",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_kb_references_chat_kb_unique",
                table: "ticket_kb_references",
                columns: new[] { "chat_id", "kb_article_id" },
                unique: true,
                filter: "chat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id_reference_type",
                table: "ticket_kb_references",
                columns: new[] { "ticket_id", "kb_article_id", "reference_type" },
                unique: true,
                filter: "chat_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_kb_references_chat_kb_unique",
                table: "ticket_kb_references");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ticket_kb_references_ticket_id_kb_article_id_reference_type\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"ix_ticket_kb_references_ticket_id_kb_article_id_reference_type\";");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "ticket_kb_references");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id_reference_type",
                table: "ticket_kb_references",
                columns: new[] { "ticket_id", "kb_article_id", "reference_type" },
                unique: true);
        }
    }
}
