using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TicketChatOperationsGH866 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_templates");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chat_mentions_user_unread",
                table: "ticket_chat_mentions");

            migrationBuilder.DropColumn(
                name: "incident_detected_from",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "incident_detected_to",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                table: "ticket_chat_mentions");

            migrationBuilder.DropColumn(
                name: "is_acknowledged",
                table: "ticket_chat_mentions");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_mentions_mentioned_user_id",
                table: "ticket_chat_mentions",
                column: "mentioned_user_id");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    is_internal_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_chat_templates", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_chat_templates_scope",
                table: "chat_templates",
                column: "scope");

            migrationBuilder.DropIndex(
                name: "ix_ticket_chat_mentions_mentioned_user_id",
                table: "ticket_chat_mentions");

            migrationBuilder.AddColumn<DateTime>(
                name: "incident_detected_from",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "incident_detected_to",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "acknowledged_at",
                table: "ticket_chat_mentions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_acknowledged",
                table: "ticket_chat_mentions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_chat_mentions_user_unread",
                table: "ticket_chat_mentions",
                columns: new[] { "mentioned_user_id", "is_acknowledged" });
        }
    }
}
