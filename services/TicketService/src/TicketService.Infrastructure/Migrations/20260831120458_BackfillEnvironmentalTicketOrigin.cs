using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillEnvironmentalTicketOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ticket sự cố môi trường tạo TRƯỚC khi có `TicketOriginEnum.AutoFromEnvironment = 5`
            // đang mang origin của thứ khác:
            //   - đường ambient (nhiệt độ / độ ẩm / gas của site) → `AutoFromAlert = 2`, tức là
            //     origin của "AI chấm bất thường một viên pin";
            //   - đường thiết bị tự báo (khói, rò khí, ngập) → `System = 4`, dùng chung với cascade
            //     risk và bảo trì định kỳ.
            //
            // Không backfill thì dòng cũ vẫn hiện nhãn "AI predicted" / đeo badge "AI suggested"
            // trên hàng chờ, đúng cái vừa sửa ở tầng code.
            //
            // Nhận diện: `impact_scope = 2` (Site) cho đường ambient, `environmental_incident_id`
            // cho đường incident. `origin` là int nên không cần đổi schema.
            migrationBuilder.Sql(@"
UPDATE tickets
SET origin = 5
WHERE is_deleted = false
  AND origin IN (2, 4)
  AND (environmental_incident_id IS NOT NULL
       OR (origin = 2 AND impact_scope = 2));");

            // Title được LƯU lúc tạo ticket, nên sửa code chỉ đổi ticket mới — dòng cũ vẫn đọc
            // "[Auto] DEMO-V2 - High Ambient Temperature", nhìn y hệt ticket bất thường của một
            // viên pin. Đổi luôn cho khớp giọng của ticket môi trường tạo bằng đường incident.
            //
            // Chỉ đụng đúng tiền tố "[Auto] " và chỉ trên dòng vừa được gán origin môi trường ở
            // trên; phần sau tiền tố giữ nguyên nên không mất thông tin nào.
            migrationBuilder.Sql(@"
UPDATE tickets
SET title = 'Environmental incident at ' || substring(title from 8)
WHERE is_deleted = false
  AND origin = 5
  AND title LIKE '[Auto] %';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trả về đúng origin cũ của TỪNG đường, không gộp một giá trị: incident vốn là
            // `System`, ambient vốn là `AutoFromAlert`.
            migrationBuilder.Sql(@"
UPDATE tickets SET origin = 4 WHERE origin = 5 AND environmental_incident_id IS NOT NULL;
UPDATE tickets SET origin = 2 WHERE origin = 5;");

            migrationBuilder.Sql(@"
UPDATE tickets
SET title = '[Auto] ' || substring(title from 30)
WHERE title LIKE 'Environmental incident at %' AND title LIKE '% - %';");
        }
    }
}
