using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAttachmentPublicUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "public_url",
                table: "ticket_attachments");

            migrationBuilder.Sql("ALTER TABLE maintenance_logs ALTER COLUMN parts_used TYPE jsonb USING (CASE WHEN parts_used IS NULL THEN NULL WHEN parts_used = '' THEN 'null'::jsonb WHEN parts_used LIKE '\"%' OR parts_used LIKE '[%' OR parts_used LIKE '{%' THEN parts_used::jsonb ELSE to_jsonb(parts_used) END);");

            migrationBuilder.AlterColumn<string>(
                name: "parts_used",
                table: "maintenance_logs",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "threshold_value",
                table: "alert_ticket_saga_states",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "actual_value",
                table: "alert_ticket_saga_states",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_url",
                table: "ticket_attachments",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "parts_used",
                table: "maintenance_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "threshold_value",
                table: "alert_ticket_saga_states",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "actual_value",
                table: "alert_ticket_saga_states",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);
        }
    }
}
