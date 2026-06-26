using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixTicketKbReferenceUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id",
                table: "ticket_kb_references");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id_reference_type",
                table: "ticket_kb_references",
                columns: new[] { "ticket_id", "kb_article_id", "reference_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id_reference_type",
                table: "ticket_kb_references");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_kb_references_ticket_id_kb_article_id",
                table: "ticket_kb_references",
                columns: new[] { "ticket_id", "kb_article_id" },
                unique: true);
        }
    }
}
