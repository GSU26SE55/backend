using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachmentEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "download_count",
                table: "ticket_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_inline",
                table: "ticket_attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_url",
                table: "ticket_attachments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "virus_scan_status",
                table: "ticket_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "download_count",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "is_inline",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "thumbnail_url",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "virus_scan_status",
                table: "ticket_attachments");
        }
    }
}
