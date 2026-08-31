using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    // [Migration] là thứ EF dùng để NHẬN DIỆN migration — file viết tay thiếu attribute này thì
    // runtime báo "Pending migrations: 0" và cột không bao giờ được tạo (xem AddAccountAvatarUrl).
    [DbContext(typeof(TicketDbContext))]
    [Migration("20260830140000_AddTicketSiteIdAndParentTicketId")]
    public partial class AddTicketSiteIdAndParentTicketId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cả hai đều nullable: ticket đã tồn tại không có giá trị để backfill, và
            // KHÔNG có default hợp lý (site nào? cha nào?) nên để null = "chưa biết".
            migrationBuilder.AddColumn<Guid>(
                name: "site_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_site_id",
                table: "tickets",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_parent_ticket_id",
                table: "tickets",
                column: "parent_ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_parent_ticket_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_site_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "parent_ticket_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "site_id",
                table: "tickets");
        }
    }
}
