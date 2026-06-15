using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 5B #236 — non-unique filtered index trên alerts(ticket_id)
    /// để Saga query "alerts chưa link" và admin reconciliation hiệu quả.
    /// Column đã tồn tại, không add.
    /// </summary>
    public partial class AddAlertTicketLinkIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE INDEX ix_alerts_ticket_id_filtered
ON alerts (ticket_id)
WHERE ticket_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_alerts_ticket_id_filtered;");
        }
    }
}
