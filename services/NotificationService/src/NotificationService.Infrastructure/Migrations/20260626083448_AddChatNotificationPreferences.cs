using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "digest_window_minutes",
                table: "notification_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_chat",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_mention",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_reaction",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "digest_window_minutes",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "notify_on_chat",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "notify_on_mention",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "notify_on_reaction",
                table: "notification_preferences");
        }
    }
}
