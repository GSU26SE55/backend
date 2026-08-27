using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProjectionSourceVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_source_event_at_utc",
                table: "staff_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_staff_profile_source_event_at_utc",
                table: "staff_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_source_event_at_utc",
                table: "customer_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_source_event_at_utc",
                table: "staff_accounts");

            migrationBuilder.DropColumn(
                name: "last_staff_profile_source_event_at_utc",
                table: "staff_accounts");

            migrationBuilder.DropColumn(
                name: "last_source_event_at_utc",
                table: "customer_accounts");
        }
    }
}
