using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    // [Migration] attribute is required — EF uses it to identify and apply this migration at runtime.
    [DbContext(typeof(TicketDbContext))]
    [Migration("20260831000000_SplitSlaTimerByType")]
    public partial class SplitSlaTimerByType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add type column — default 1 (Response) so existing rows are valid immediately.
            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "sla_timers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // 2. Data migration: tickets that are past the Open/Pending stages already have their
            //    Response SLA settled. Any SlaTimer linked to those tickets was being reused as the
            //    Resolution timer, so mark it accordingly.
            //    Statuses: Open=1, Pending=2 → keep Response (type=1)
            //    InProgress=3, Request=4, ReAssign=5, Completed=6, Closed=7, ClosedRejected=8 → Resolution (type=2)
            migrationBuilder.Sql(@"
                UPDATE sla_timers s
                SET type = 2
                FROM tickets t
                WHERE s.ticket_id = t.id
                  AND t.status NOT IN (1, 2)
                  AND t.is_deleted = false;
            ");

            // 3. Unique index (ticket_id, type) — guarantees at most one Response and one Resolution
            //    timer per ticket.
            migrationBuilder.CreateIndex(
                name: "ux_sla_timers_ticket_type",
                table: "sla_timers",
                columns: ["ticket_id", "type"],
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sla_timers_ticket_type",
                table: "sla_timers");

            migrationBuilder.DropColumn(
                name: "type",
                table: "sla_timers");
        }
    }
}
