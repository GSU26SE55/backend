namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Chạy AI verify (chấm điểm thật/rác + dò trùng) cho 1 ticket đang Pending → update kết quả.
/// Dùng chung: consumer async (TicketCreatedEvent) + re-verify thủ công (Manager bấm nút).
/// </summary>
public interface ITicketVerifyRunner
{
    /// <summary>
    /// Verify ticket theo id. Không làm gì nếu ticket không tồn tại / không Pending / không phải manual.
    /// Tự cập nhật AiVerifyStatus + score + reason + nghi trùng và SaveChanges.
    /// </summary>
    Task RunAsync(Guid ticketId, CancellationToken ct = default);
}
