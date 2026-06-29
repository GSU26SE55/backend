using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_metrics_daily",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_date = table.Column<DateOnly>(type: "date", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    avg_response_time_min = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    internal_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    mention_received_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_metrics_daily", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_metrics_daily_date_staff",
                table: "chat_metrics_daily",
                columns: new[] { "metric_date", "staff_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_metrics_daily");
        }
    }
}
