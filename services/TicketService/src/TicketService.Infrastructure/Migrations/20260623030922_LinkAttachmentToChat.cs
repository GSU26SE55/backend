using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkAttachmentToChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "chat_id",
                table: "ticket_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_chat_id",
                table: "ticket_attachments",
                column: "chat_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ticket_attachments_ticket_chats_chat_id",
                table: "ticket_attachments",
                column: "chat_id",
                principalTable: "ticket_chats",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ticket_attachments_ticket_chats_chat_id",
                table: "ticket_attachments");

            migrationBuilder.DropIndex(
                name: "IX_ticket_attachments_chat_id",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "ticket_attachments");
        }
    }
}
