using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GH1244PeriodicMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_customer_scheduled_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_due_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_manager_escalated_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_reminder_1_sent_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_reminder_2_sent_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "periodic_maintenance_schedule_deadline_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "periodic_maintenance_source_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_tickets_periodic_maintenance_battery_due",
                table: "tickets",
                columns: new[] { "battery_asset_id", "periodic_maintenance_due_at_utc" },
                unique: true,
                filter: "is_deleted = false AND periodic_maintenance_due_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tickets_periodic_maintenance_battery_due",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_customer_scheduled_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_due_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_manager_escalated_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_reminder_1_sent_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_reminder_2_sent_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_schedule_deadline_at_utc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "periodic_maintenance_source_ticket_id",
                table: "tickets");
        }
    }
}
