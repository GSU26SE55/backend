using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameTicketCommentsToTicketChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ticket_comments",
                newName: "ticket_chats");

            migrationBuilder.RenameIndex(
                table: "ticket_chats",
                name: "IX_ticket_comments_author_user_id",
                newName: "IX_ticket_chats_author_user_id");

            migrationBuilder.RenameIndex(
                table: "ticket_chats",
                name: "IX_ticket_comments_ticket_id",
                newName: "IX_ticket_chats_ticket_id");

            migrationBuilder.Sql(
                "ALTER TABLE ticket_chats RENAME CONSTRAINT \"PK_ticket_comments\" TO \"PK_ticket_chats\";");

            migrationBuilder.Sql(
                "ALTER TABLE ticket_chats RENAME CONSTRAINT \"FK_ticket_comments_tickets_ticket_id\" TO \"FK_ticket_chats_tickets_ticket_id\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ticket_chats RENAME CONSTRAINT \"FK_ticket_chats_tickets_ticket_id\" TO \"FK_ticket_comments_tickets_ticket_id\";");

            migrationBuilder.Sql(
                "ALTER TABLE ticket_chats RENAME CONSTRAINT \"PK_ticket_chats\" TO \"PK_ticket_comments\";");

            migrationBuilder.RenameIndex(
                table: "ticket_chats",
                name: "IX_ticket_chats_ticket_id",
                newName: "IX_ticket_comments_ticket_id");

            migrationBuilder.RenameIndex(
                table: "ticket_chats",
                name: "IX_ticket_chats_author_user_id",
                newName: "IX_ticket_comments_author_user_id");

            migrationBuilder.RenameTable(
                name: "ticket_chats",
                newName: "ticket_comments");
        }
    }
}
