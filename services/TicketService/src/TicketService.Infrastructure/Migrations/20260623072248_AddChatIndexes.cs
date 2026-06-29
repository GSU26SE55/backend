using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ticket_chats_author_created_at",
                table: "ticket_chats",
                columns: new[] { "author_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chats_ticket_created_at",
                table: "ticket_chats",
                columns: new[] { "ticket_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chats_ticket_pinned_created_at",
                table: "ticket_chats",
                columns: new[] { "ticket_id", "is_pinned", "created_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_chats_author_created_at",
                table: "ticket_chats");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chats_ticket_created_at",
                table: "ticket_chats");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chats_ticket_pinned_created_at",
                table: "ticket_chats");
        }
    }
}
