using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTicketSchemaGH85 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "ticket_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ticket_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ticket_activities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ticket_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkillTier",
                table: "staff_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "sla_timers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "sla_timers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "sla_timers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "sla_timers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "sla_timers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ticket_activities");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ticket_activities");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ticket_activities");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ticket_activities");

            migrationBuilder.DropColumn(
                name: "SkillTier",
                table: "staff_accounts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "sla_timers");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "sla_timers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "sla_timers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "sla_timers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "sla_timers");
        }
    }
}
