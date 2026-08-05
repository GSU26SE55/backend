using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Setting;

/// <summary>
/// Đường vận chuyển push đang áp dụng, kèm đủ dữ liệu để màn hình Admin dựng ô chọn mà không phải
/// hard-code danh sách lựa chọn ở phía frontend.
/// </summary>
public class PushTransportDto
{
    /// <summary>Giá trị đang áp dụng.</summary>
    public PushTransportEnum Transport { get; set; }

    /// <summary>Tên hằng số tương ứng ("SignalR" / "Expo" / "Both") — tiện cho frontend hiển thị.</summary>
    public string TransportName { get; set; } = string.Empty;

    /// <summary>Toàn bộ lựa chọn hợp lệ, theo đúng thứ tự khai báo trong enum.</summary>
    public List<PushTransportOptionDto> Options { get; set; } = new();
}

/// <summary>Một lựa chọn transport kèm mô tả tiếng Việt để hiện thẳng lên giao diện.</summary>
public class PushTransportOptionDto
{
    public PushTransportEnum Value { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Đường này có cần device token của thiết bị không — frontend dùng để cảnh báo người vận hành.</summary>
    public bool RequiresDeviceToken { get; set; }
}

/// <summary>Response cho cả GET lẫn PUT push transport.</summary>
public class PushTransportResponse : CommonResponse<PushTransportDto> { }
