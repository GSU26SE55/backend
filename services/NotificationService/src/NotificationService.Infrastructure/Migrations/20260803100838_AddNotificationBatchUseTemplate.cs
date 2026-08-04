using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationBatchUseTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "use_template",
                table: "notification_batches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "use_template",
                table: "notification_batches");
        }
    }
}
