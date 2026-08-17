namespace TicketService.Domain.Enums;

/// <summary>
/// Loại nhật ký bảo trì.
/// </summary>
public enum MaintenanceLogTypeEnum
{
    /// <summary>Hỗ trợ từ xa.</summary>
    RemoteSupport = 1,
    /// <summary>Xử lý tại chỗ.</summary>
    OnSite = 2,
    /// <summary>Thay thế linh kiện.</summary>
    PartReplacement = 3,
    /// <summary>Kiểm tra định kỳ.</summary>
    Inspection = 4,
    /// <summary>Log tự động/tạo khi Staff hoàn tất (complete) ticket.</summary>
    Completion = 5
}
