namespace BatteryService.Application.Import;

/// <summary>Đẩy một lô import đi tiếp một bước trong pha ghi thật.</summary>
public interface IImportCommitService
{
    /// <summary>
    /// Xử lý lô một nhịp.
    /// </summary>
    /// <returns>
    /// True khi lô đã đi tới trạng thái cuối (xong, xong-có-lỗi, hoặc hỏng) và không cần gọi lại.
    /// False khi lô còn đang chờ — điển hình là chờ bản sao khách hàng đồng bộ từ AuthService.
    /// </returns>
    Task<bool> AdvanceAsync(Guid batchId, CancellationToken cancellationToken);
}
