using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationCategoryPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_category_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    push_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sms_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    in_app_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_category_preferences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_notification_category_preferences_user_category",
                table: "notification_category_preferences",
                columns: new[] { "user_id", "category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_category_preferences");
        }
    }
}
