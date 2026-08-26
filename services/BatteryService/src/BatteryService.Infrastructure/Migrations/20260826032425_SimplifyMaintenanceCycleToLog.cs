using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyMaintenanceCycleToLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "outcome_summary",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "ticket_code",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "ticket_id",
                table: "maintenance_cycles");

            migrationBuilder.RenameColumn(
                name: "soh_percent_at_completion",
                table: "maintenance_cycles",
                newName: "soh_percent_at_cycle");

            migrationBuilder.AddColumn<DateTime>(
                name: "recorded_at_utc",
                table: "maintenance_cycles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // recorded_at_utc là NOT NULL nên EF đặt mặc định 0001-01-01 cho mọi dòng sẵn
            // có. Các kỳ đã ghi trước đây không lưu lại thời điểm ghi, nên lấy created_at
            // làm xấp xỉ — đúng về mặt thứ tự thời gian và không để lại mốc năm 1.
            migrationBuilder.Sql(
                "UPDATE maintenance_cycles SET recorded_at_utc = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recorded_at_utc",
                table: "maintenance_cycles");

            migrationBuilder.RenameColumn(
                name: "soh_percent_at_cycle",
                table: "maintenance_cycles",
                newName: "soh_percent_at_completion");

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at_utc",
                table: "maintenance_cycles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome_summary",
                table: "maintenance_cycles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ticket_code",
                table: "maintenance_cycles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ticket_id",
                table: "maintenance_cycles",
                type: "uuid",
                nullable: true);
        }
    }
}
