using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceCycleTicketLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Không có khoá ngoại: ticket nằm ở TicketService, ràng buộc chéo database sẽ
            // khoá hai service vào nhau. Cột điền bất đồng bộ nên phải nullable.
            migrationBuilder.AddColumn<Guid>(
                name: "ticket_id",
                table: "maintenance_cycles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_cycles_ticket",
                table: "maintenance_cycles",
                column: "ticket_id",
                filter: "ticket_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_maintenance_cycles_ticket",
                table: "maintenance_cycles");

            migrationBuilder.DropColumn(
                name: "ticket_id",
                table: "maintenance_cycles");
        }
    }
}
