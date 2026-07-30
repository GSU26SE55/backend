using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateVersioningAndAccountLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notification_templates_type_channel_locale",
                table: "notification_templates");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "notification_templates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "preferred_locale",
                table: "account_read_models",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates",
                columns: new[] { "type", "channel", "locale" },
                unique: true,
                filter: "is_active = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_type_channel_locale_version",
                table: "notification_templates",
                columns: new[] { "type", "channel", "locale", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates");

            migrationBuilder.DropIndex(
                name: "ux_notification_templates_type_channel_locale_version",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "version",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "preferred_locale",
                table: "account_read_models");

            migrationBuilder.CreateIndex(
                name: "IX_notification_templates_type_channel_locale",
                table: "notification_templates",
                columns: new[] { "type", "channel", "locale" },
                unique: true);
        }
    }
}
