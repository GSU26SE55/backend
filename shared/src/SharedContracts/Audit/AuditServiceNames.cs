namespace SharedContracts.Audit;

/// <summary>
/// GH-728 — danh sách service có bảng audit source-of-truth và publish
/// <c>AuditCreatedEventV1</c>. Dùng chung cho cả hai đầu của luồng replay:
/// AuditAggregatorService dựa vào đây để biết phải CHỜ bao nhiêu service báo xong,
/// còn mỗi service dùng <see cref="Matches"/> để biết yêu cầu replay có dành cho mình không.
///
/// <para>Đặt ở SharedContracts vì nếu hai đầu tự giữ danh sách riêng thì thêm/bớt service
/// sẽ làm job replay treo vĩnh viễn ở trạng thái "đang chờ" mà không ai nhận ra.</para>
/// </summary>
public static class AuditServiceNames
{
    public const string Auth = "AuthService";
    public const string Battery = "BatteryService";
    public const string Ticket = "TicketService";
    public const string Notification = "NotificationService";
    public const string Sms = "SmsService";
    public const string FileStorage = "FileStorageService";

    /// <summary>Toàn bộ service có audit source-of-truth (6).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Auth, Battery, Ticket, Notification, Sms, FileStorage
    };

    /// <summary>
    /// True nếu yêu cầu replay áp dụng cho <paramref name="serviceName"/>.
    /// <paramref name="requestedService"/> rỗng/null = replay TẤT CẢ.
    /// So sánh không phân biệt hoa thường để tránh lệch do người gõ tay vào API admin.
    /// </summary>
    public static bool Matches(string? requestedService, string serviceName)
        => string.IsNullOrWhiteSpace(requestedService)
           || string.Equals(requestedService.Trim(), serviceName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True nếu tên service là một trong các service có audit (dùng để validate input).</summary>
    public static bool IsKnown(string? serviceName)
        => !string.IsNullOrWhiteSpace(serviceName)
           && All.Any(s => string.Equals(s, serviceName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Số service sẽ phản hồi cho một yêu cầu replay.</summary>
    public static int ExpectedResponders(string? requestedService)
        => string.IsNullOrWhiteSpace(requestedService) ? All.Count : 1;
}
