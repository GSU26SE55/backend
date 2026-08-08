using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVirusScanRetryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "virus_scan_attempts",
                table: "ticket_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "virus_scan_last_attempt_at",
                table: "ticket_attachments",
                type: "timestamp with time zone",
                nullable: true);

            // GH-790 — PHỤC HỒI dữ liệu đã kẹt vì chính lỗi này.
            //
            // Worker cũ tải file bằng GET /api/files/{id}/download mà không gắn token, trong khi
            // endpoint đó có [Authorize] ⇒ MỌI lần quét đều 401 và bản ghi bị ghi thẳng thành
            // Failed(4). Worker chỉ quét bản ghi Pending(1) nên chúng không bao giờ được thử lại:
            // đính kèm vĩnh viễn không tải được. Thêm cột không tự cứu được những dòng đó.
            //
            // Đưa chúng về Pending(1) với số lần thử = 0 để lượt quét tới nhặt lên. An toàn: file
            // thật sự không quét được sẽ tự quay lại Failed sau khi hết MaxAttempts — lần này là
            // kết luận có căn cứ, không phải hệ quả của một lỗi xác thực.
            migrationBuilder.Sql(@"
UPDATE ticket_attachments
SET virus_scan_status = 1,
    virus_scan_attempts = 0,
    virus_scan_last_attempt_at = NULL
WHERE virus_scan_status = 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục lại Failed: dữ liệu cũ đó là hệ quả của một lỗi xác thực, dựng lại nó
            // chỉ làm đính kèm kẹt trở lại. Bỏ hai cột là đủ để quay về đúng lược đồ trước đó.
            migrationBuilder.DropColumn(
                name: "virus_scan_attempts",
                table: "ticket_attachments");

            migrationBuilder.DropColumn(
                name: "virus_scan_last_attempt_at",
                table: "ticket_attachments");
        }
    }
}
