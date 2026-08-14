namespace AuthService.Application.Common.Options;

/// <summary>
/// GH-776 — thông tin xác thực cho endpoint OAuth 2.0 Token Introspection.
/// </summary>
/// <remarks>
/// <para>
/// RFC 7662 §2.1 nói thẳng: endpoint introspection PHẢI yêu cầu một dạng ủy quyền nào đó. Bản cũ
/// không có <c>[Authorize]</c>, không API key, không rate-limit — bất kỳ ai trên Internet cũng gọi
/// được và biết ngay một token còn sống hay đã bị thu hồi. Đo được lúc chạy thật: 12 request liên
/// tiếp không kèm Authorization đều trả 200 kèm <c>active=true</c>.
/// </para>
/// <para>
/// Đây là bí mật DÙNG CHUNG giữa AuthService và các resource server nội bộ — khác hẳn API key của
/// thiết bị IoT (mỗi thiết bị một khoá, xoay được, lưu trong DB). Introspection không có "thiết bị"
/// nào để gắn khoá vào, nên cấu hình là chỗ đúng.
/// </para>
/// </remarks>
public class IntrospectionOptions
{
    public const string SectionName = "Introspection";

    /// <summary>
    /// Khoá mà resource server phải gửi qua header <see cref="HeaderName"/>.
    /// </summary>
    /// <remarks>
    /// Bỏ trống ⇒ endpoint TỪ CHỐI TẤT CẢ (fail closed). Cố ý không mặc định mở: một endpoint
    /// introspection mở sẵn khi thiếu cấu hình chính là lỗi đang được sửa ở đây. Hiện chưa service
    /// nào trong hệ thống gọi endpoint này (đã tìm toàn repo), nên fail closed không làm hỏng gì.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>Header mang khoá. Tách khỏi <c>Authorization</c> để không lẫn với JWT người dùng.</summary>
    public const string HeaderName = "X-Introspection-Key";

    /// <summary>Độ dài tối thiểu chấp nhận được — chặn kiểu đặt khoá "123" cho có.</summary>
    public const int MinKeyLength = 32;

    /// <summary>True khi khoá đã được cấu hình đủ mạnh để dùng.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Trim().Length >= MinKeyLength;
}
