using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    // [Migration] là thứ EF dùng để NHẬN DIỆN migration; file này viết tay, thiếu attribute nên
    // runtime báo "Pending migrations: 0" và cột avatar_url không bao giờ được tạo — service crash
    // ngay lúc seed với 'column c.avatar_url does not exist'.
    [DbContext(typeof(TicketDbContext))]
    [Migration("20260829120000_AddAccountAvatarUrl")]
    public partial class AddAccountAvatarUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "customer_accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "staff_accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "staff_accounts");
        }
    }
}
