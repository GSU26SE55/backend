namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Gọi BatteryService để lấy serial number của pin (denormalize snapshot vào Ticket).
/// Fail/exception → trả null (KHÔNG chặn tạo ticket).
/// </summary>
public interface IBatteryLookupClient
{
    /// <summary>
    /// Lấy serial number của battery asset theo id. Forward JWT của request hiện tại để BatteryService
    /// authz (Customer có quyền xem pin của mình). Null nếu không tìm thấy / lỗi.
    /// </summary>
    Task<string?> GetSerialAsync(Guid assetId, CancellationToken ct);
}
