using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatThreading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_chat_id",
                table: "ticket_chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reply_count",
                table: "ticket_chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "thread_root_id",
                table: "ticket_chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chats_parent",
                table: "ticket_chats",
                column: "parent_chat_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chats_thread_root",
                table: "ticket_chats",
                column: "thread_root_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ticket_chats_ticket_chats_parent_chat_id",
                table: "ticket_chats",
                column: "parent_chat_id",
                principalTable: "ticket_chats",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ticket_chats_ticket_chats_parent_chat_id",
                table: "ticket_chats");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chats_parent",
                table: "ticket_chats");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chats_thread_root",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "parent_chat_id",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "reply_count",
                table: "ticket_chats");

            migrationBuilder.DropColumn(
                name: "thread_root_id",
                table: "ticket_chats");
        }
    }
}
