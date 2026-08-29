using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TicketService.Infrastructure.Persistence;

#nullable disable

namespace TicketService.Infrastructure.Migrations
{
    /// <summary>
    /// Bỏ TicketOriginEnum.CreatedByStaff (=3): staff tạo hộ khách nay ghi thẳng
    /// ManualByCustomer (=1), UI không tách hai loại này nữa.
    ///
    /// Cột origin lưu dạng int, nên xoá member khỏi enum mà không dọn dữ liệu thì các dòng
    /// còn giữ 3 sẽ được EF đọc ra thành (TicketOriginEnum)3 — một giá trị không thuộc enum,
    /// lọt qua mọi switch mà không ném lỗi. Migration này dồn chúng về 1.
    /// </summary>
    [DbContext(typeof(TicketDbContext))]
    [Migration("20260829130000_MergeCreatedByStaffOriginIntoManual")]
    public partial class MergeCreatedByStaffOriginIntoManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE tickets SET origin = 1 WHERE origin = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục được: sau khi dồn về 1, ticket staff-tạo-hộ lẫn với ticket khách
            // tự tạo và không còn dấu hiệu nào tách lại. Down để trống một cách CÓ CHỦ Ý —
            // rollback vẫn chạy, chỉ là dữ liệu giữ nguyên ở trạng thái đã gộp.
        }
    }
}
