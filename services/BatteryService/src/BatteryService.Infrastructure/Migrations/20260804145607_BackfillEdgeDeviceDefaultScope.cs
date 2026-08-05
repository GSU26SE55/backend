using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BatteryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillEdgeDeviceDefaultScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "api_key_scopes",
                table: "iot_devices",
                type: "integer",
                nullable: false,
                defaultValue: 15,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 11);

            // GH-785 — AlterColumn ở trên chỉ đổi mặc định cho dòng MỚI. Thiết bị đã tạo trước bản
            // sửa vẫn mang scope 11 và tiếp tục bị chặn khi báo khói/gas/rò nước — dù firmware xuất
            // xưởng đã có sẵn SHT31, MQ2 và cảm biến rò nước.
            //
            // CHỈ nâng dòng có ĐÚNG giá trị default cũ (11) → 15. Thiết bị được cấp scope tuỳ chỉnh
            // giữ nguyên: người vận hành cố ý thu hẹp quyền thì không được âm thầm mở rộng lại.
            migrationBuilder.Sql(@"
                UPDATE iot_devices
                SET api_key_scopes = 15
                WHERE is_deleted = false
                  AND api_key_scopes = 11;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "api_key_scopes",
                table: "iot_devices",
                type: "integer",
                nullable: false,
                defaultValue: 11,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 15);

            // Đảo đúng tập vừa đụng. Lưu ý trung thực: thiết bị vốn ĐÃ có scope 15 từ trước (do
            // người vận hành tự đặt) cũng bị hạ về 11 — sau khi Up() chạy thì hai nhóm mang cùng
            // giá trị nên không phân biệt được nữa. Chấp nhận được: đây là nới quyền cho đúng phần
            // cứng đang có, không phải thay đổi lược đồ có thể mất dữ liệu.
            migrationBuilder.Sql(@"
                UPDATE iot_devices
                SET api_key_scopes = 11
                WHERE is_deleted = false
                  AND api_key_scopes = 15;
            ");
        }
    }
}
