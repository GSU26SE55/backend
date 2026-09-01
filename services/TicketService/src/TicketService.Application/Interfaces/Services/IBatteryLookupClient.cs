namespace TicketService.Application.Interfaces.Services;

/// <summary>Snapshot của battery asset cần denormalize vào Ticket. Field nào không đọc được thì null.</summary>
public record BatteryLookupResult(string? SerialNumber, Guid? SiteId);

/// <summary>
/// Gọi BatteryService để lấy snapshot của pin (denormalize vào Ticket).
/// Fail/exception → trả null (KHÔNG chặn tạo ticket).
/// </summary>
public interface IBatteryLookupClient
{
    /// <summary>
    /// Lấy serial number của battery asset theo id. Forward JWT của request hiện tại để BatteryService
    /// authz (Customer có quyền xem pin của mình). Null nếu không tìm thấy / lỗi.
    /// </summary>
    Task<string?> GetSerialAsync(Guid assetId, CancellationToken ct);

    /// <summary>
    /// Như <see cref="GetSerialAsync"/> nhưng lấy cả SiteId — cùng một response của
    /// GET /api/battery-assets/{id}, nên KHÔNG tốn thêm round-trip.
    ///
    /// Ticket cần SiteId để gom được với ticket environmental cùng cabinet; ticket environmental
    /// không có pin nào nên đó là đường duy nhất nối hai loại lại.
    /// </summary>
    Task<BatteryLookupResult> GetSnapshotAsync(Guid assetId, CancellationToken ct);
}
