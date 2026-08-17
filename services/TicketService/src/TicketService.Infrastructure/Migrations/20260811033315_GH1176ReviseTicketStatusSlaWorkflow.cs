using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GH1176ReviseTicketStatusSlaWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "active_incident_episode_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_context",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_reason",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schedule_version",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_start_at_utc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE tickets
                SET status = 2,
                    pending_context = 2,
                    pending_reason = CASE WHEN status = 5 THEN 1 ELSE 2 END
                WHERE status IN (5, 6, 7);

                UPDATE tickets SET status = 6 WHERE status = 8;
                UPDATE tickets SET status = 5 WHERE status = 9;
                UPDATE tickets SET status = 7 WHERE status = 10;
                UPDATE tickets SET status = 7 WHERE status = 11;
                UPDATE tickets SET status = 8 WHERE status = 12;

                UPDATE sla_pause_events
                SET reason = CASE
                    WHEN reason = 3 THEN 2
                    WHEN reason = 4 THEN 1
                    ELSE reason
                END
                WHERE reason IN (3, 4);

                UPDATE tickets
                SET status = 5,
                    priority = 4,
                    is_incident = TRUE,
                    active_incident_episode_id = COALESCE(active_incident_episode_id, gen_random_uuid())
                WHERE status = 13;

                UPDATE sla_timers AS timer
                SET status = 5,
                    current_pause_started_at = NULL
                FROM tickets AS ticket
                WHERE timer.ticket_id = ticket.id
                  AND ticket.priority = 4
                  AND timer.status IN (1, 2);

                UPDATE sla_timers AS timer
                SET status = 2,
                    current_pause_started_at = COALESCE(timer.current_pause_started_at, NOW())
                FROM tickets AS ticket
                WHERE timer.ticket_id = ticket.id
                  AND ticket.status <> 3
                  AND ticket.priority <> 4
                  AND timer.status = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_due_activation",
                table: "tickets",
                columns: new[] { "status", "scheduled_start_at_utc" },
                filter: "is_deleted = false AND status = 2 AND scheduled_start_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_due_activation",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "active_incident_episode_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "pending_context",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "pending_reason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "schedule_version",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "scheduled_start_at_utc",
                table: "tickets");
        }
    }
}
