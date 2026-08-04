using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Migrations
{
    /// <summary>
    /// 02/08/2026 — bỏ hẳn locale khỏi notification template (hệ thống tiếng Việt only).
    ///
    /// <para><b>Cảnh báo — bản EF sinh tự động KHÔNG chạy được.</b> Nó drop cột <c>locale</c> rồi tạo
    /// unique index <c>(type, channel, version)</c>; nhưng mỗi cặp (type, channel) đang có tới 2 dòng
    /// version = 1 (một vi-VN, một en-US). Đo trên DB thật ngày 02/08/2026: 121 dòng = 82 vi-VN + 39
    /// en-US ⇒ <b>39 cặp trùng khoá</b>, index sẽ dựng thất bại và migration rollback giữa chừng.
    /// Vì vậy phải XOÁ các dòng không phải vi-VN TRƯỚC — và phải xoá trước khi drop cột, vì sau đó
    /// không còn gì để lọc theo.</para>
    ///
    /// <para><b>Xoá cứng, không soft-delete.</b> Index <c>ux_notification_templates_type_channel_version</c>
    /// không có filter <c>is_deleted</c>, nên đánh dấu <c>is_deleted = true</c> vẫn trùng khoá y như cũ.</para>
    ///
    /// <para>An toàn: không bảng nào có khoá ngoại trỏ tới <c>notification_templates</c> (đã kiểm tra
    /// <c>information_schema</c>), và <c>account_read_models.preferred_locale</c> đang null ở 100% dòng
    /// — chưa consumer nào từng ghi vào cột đó.</para>
    /// </summary>
    public partial class RemoveTemplateLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BẮT BUỘC chạy trước DropColumn: dọn các bản dịch để (type, channel, version) trở lại duy nhất.
            migrationBuilder.Sql(
                "DELETE FROM notification_templates WHERE locale IS DISTINCT FROM 'vi-VN';");

            migrationBuilder.DropIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates");

            migrationBuilder.DropIndex(
                name: "ux_notification_templates_type_channel_locale_version",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "locale",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "preferred_locale",
                table: "account_read_models");

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates",
                columns: new[] { "type", "channel" },
                unique: true,
                filter: "is_active = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_type_channel_version",
                table: "notification_templates",
                columns: new[] { "type", "channel", "version" },
                unique: true);
        }

        /// <summary>
        /// Rollback dựng lại được SCHEMA nhưng KHÔNG dựng lại được DỮ LIỆU: các bản en-US đã bị xoá cứng
        /// ở <c>Up</c>. Sau khi rollback, chạy lại seeder của bản code cũ để sinh lại chúng.
        /// <c>locale</c> dựng lại với default <c>vi-VN</c> để các dòng đang có không vi phạm NOT NULL.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates");

            migrationBuilder.DropIndex(
                name: "ux_notification_templates_type_channel_version",
                table: "notification_templates");

            migrationBuilder.AddColumn<string>(
                name: "locale",
                table: "notification_templates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "vi-VN");

            migrationBuilder.AddColumn<string>(
                name: "preferred_locale",
                table: "account_read_models",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_active_per_key",
                table: "notification_templates",
                columns: new[] { "type", "channel", "locale" },
                unique: true,
                filter: "is_active = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_notification_templates_type_channel_locale_version",
                table: "notification_templates",
                columns: new[] { "type", "channel", "locale", "version" },
                unique: true);
        }
    }
}
