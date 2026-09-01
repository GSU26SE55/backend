using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    // [Migration] attribute is required — EF uses it to identify and apply this migration at runtime.
    [DbContext(typeof(TicketDbContext))]
    [Migration("20260901120000_DropStaleSlaTimerTicketIdUniqueIndex")]
    public partial class DropStaleSlaTimerTicketIdUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 20260831000000_SplitSlaTimerByType added the (ticket_id, type) unique index but never
            // dropped the older per-ticket unique index "IX_sla_timers_ticket_id" (created by
            // 20260517105233_InitialTicketSchema, renamed by 20260602131049). The stale UNIQUE index
            // still enforces "one SlaTimer per ticket", so inserting the second (Resolution) timer for
            // a ticket that already has a Response timer fails with
            //   23505 duplicate key value violates unique constraint "IX_sla_timers_ticket_id"
            // even though (ticket_id, type) is distinct. The C# model (SlaTimerConfiguration) already
            // expects this index to be a plain non-unique index, so recreate it that way to match.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_sla_timers_ticket_id"";");
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_sla_timers_ticket_id"" ON sla_timers (ticket_id);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SplitSlaTimerByType now creates the non-unique lookup index itself, so rolling back
            // only this compatibility migration is intentionally a no-op. Recreating the legacy
            // UNIQUE index would fail as soon as a ticket has both Response and Resolution timers.
            // Keeping the non-unique index preserves all timer data and still supports lookups.
        }
    }
}
