using AuthService.Domain.Entities;

namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// GH-794 — giành quyền publish một dòng outbox, để hai replica không cùng gửi một message.
/// </summary>
/// <remarks>
/// <para>
/// Relay trước đây chỉ lọc <c>ProcessedAt == null</c> rồi publish. <c>ProcessedAt</c> chỉ được ghi
/// SAU khi publish xong, nên trong khoảng giữa hai việc đó mọi replica khác đều thấy dòng này vẫn
/// còn "chưa xử lý" và cùng publish. Không có gì trong bản ghi cho thấy chuyện đó đã xảy ra.
/// </para>
/// <para>
/// Cùng khuôn đã chạy ở TicketService: điều kiện nằm ngay trong câu <c>UPDATE</c> nên cơ sở dữ liệu
/// làm trọng tài, và mọi thao tác kết thúc đều đối chiếu chủ sở hữu để một instance đã mất quyền
/// (vì treo quá lâu) không ghi đè lên việc của instance đang giữ.
/// </para>
/// </remarks>
public interface IOutboxClaimService
{
    /// <summary>
    /// Giành quyền giữ dòng. Trả <c>null</c> nếu người khác đang giữ hoặc dòng đã xử lý xong.
    /// </summary>
    Task<OutboxMessage?> TryClaimAsync(Guid messageId, string leaseOwner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu đã publish xong — chỉ khi ta vẫn là chủ.</summary>
    Task<bool> MarkProcessedAsync(Guid messageId, string leaseOwner, CancellationToken cancellationToken = default);

    /// <summary>Ghi nhận thất bại, tăng số lần thử và nhả quyền — chỉ khi ta vẫn là chủ.</summary>
    Task<bool> MarkFailedAsync(Guid messageId, string leaseOwner, string error,
        CancellationToken cancellationToken = default);
}
