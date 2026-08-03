using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertAiEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_evidence",
                table: "alerts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_evidence",
                table: "alerts");
        }
    }
}
