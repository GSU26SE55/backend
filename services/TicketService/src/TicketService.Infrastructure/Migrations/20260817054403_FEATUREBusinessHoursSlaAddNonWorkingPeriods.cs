using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FEATUREBusinessHoursSlaAddNonWorkingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sla_non_working_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_non_working_periods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sla_non_working_periods_is_deleted",
                table: "sla_non_working_periods",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_sla_non_working_periods_start_date_end_date",
                table: "sla_non_working_periods",
                columns: new[] { "start_date", "end_date" });

            migrationBuilder.Sql("""
                ALTER TABLE sla_non_working_periods
                ADD CONSTRAINT ex_sla_non_working_periods_no_active_overlap
                EXCLUDE USING gist (
                    daterange(start_date, end_date, '[]') WITH &&
                )
                WHERE (is_deleted = FALSE);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sla_non_working_periods");
        }
    }
}
