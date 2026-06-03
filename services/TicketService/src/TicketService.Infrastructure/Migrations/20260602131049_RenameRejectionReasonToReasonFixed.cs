using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRejectionReasonToReasonFixed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sla_timers_ticket_id",
                table: "sla_timers");

            migrationBuilder.CreateIndex(
                name: "IX_sla_timers_ticket_id",
                table: "sla_timers",
                column: "ticket_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sla_timers_ticket_id",
                table: "sla_timers");

            migrationBuilder.CreateIndex(
                name: "IX_sla_timers_ticket_id",
                table: "sla_timers",
                column: "ticket_id");
        }
    }
}
