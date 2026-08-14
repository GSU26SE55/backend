using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Cấu hình cấp hệ thống của NotificationService, sửa được lúc chạy qua REST (màn hình Admin) thay
/// vì phải sửa appsettings rồi khởi động lại.
///
/// <para>Dạng khoá–giá trị vì các cấu hình loại này rất ít và không có quan hệ với nhau; dựng mỗi
/// thứ một bảng/cột riêng sẽ kéo theo một migration cho mỗi lần thêm một công tắc.</para>
///
/// <para>Khoá dùng dạng chấm phân cấp, khai báo tập trung trong
/// <c>NotificationSettingKeys</c> — không rải chuỗi thô trong code.</para>
/// </summary>
public class NotificationSetting : AuditableEntity
{
    /// <summary>Khoá cấu hình, duy nhất. Ví dụ <c>push.transport</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Giá trị dạng chuỗi. Kiểu thật do lớp service đọc nó quyết định (enum, số, bool…) — lưu chuỗi
    /// để một bảng phục vụ được mọi loại cấu hình mà không cần đổi schema.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Mô tả ngắn cho người vận hành đọc, hiển thị trên màn hình Admin.</summary>
    public string? Description { get; set; }
}
