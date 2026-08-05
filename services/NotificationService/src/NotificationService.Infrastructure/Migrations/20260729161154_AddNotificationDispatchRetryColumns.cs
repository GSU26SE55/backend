using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDispatchRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dispatch_attempt_count",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_dispatch_queue",
                table: "notifications",
                columns: new[] { "status", "next_attempt_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_dispatch_queue",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dispatch_attempt_count",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "notifications");
        }
    }
}
