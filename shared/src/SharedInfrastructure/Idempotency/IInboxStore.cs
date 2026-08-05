namespace SharedInfrastructure.Idempotency;

/// <summary>
/// GH-764 — trạng thái một lần xin xử lý message.
/// </summary>
public enum InboxClaimStatus
{
    /// <summary>Ta giành được quyền xử lý — chạy side effect, rồi chốt hoặc nhả.</summary>
    Claimed = 1,

    /// <summary>Đã có người xử lý XONG message này — bỏ qua, đúng nghĩa trùng lặp.</summary>
    AlreadyCompleted = 2,

    /// <summary>
    /// Người khác đang giữ chỗ và CHƯA xong. Không được bỏ qua — bỏ qua là ACK một message mà
    /// side effect có thể chưa bao giờ chạy. Phải để message quay lại sau.
    /// </summary>
    InProgressElsewhere = 3,
}

/// <summary>Kết quả xin xử lý. <see cref="Token"/> chỉ có nghĩa khi <see cref="Status"/> = Claimed.</summary>
/// <param name="Token">
/// Dấu sở hữu chỗ giữ. Chốt/nhả đều phải kèm dấu này để không giẫm lên chỗ giữ của người khác
/// trong trường hợp chỗ giữ của ta đã hết hạn và ai đó đã nhận lại message.
/// </param>
public readonly record struct InboxClaim(InboxClaimStatus Status, string Token)
{
    public static InboxClaim Completed => new(InboxClaimStatus.AlreadyCompleted, string.Empty);
    public static InboxClaim Busy => new(InboxClaimStatus.InProgressElsewhere, string.Empty);
}

/// <summary>
/// Sổ ghi message đã xử lý (Inbox pattern).
/// </summary>
/// <remarks>
/// GH-764 — trước đây chỉ có một phép "đánh dấu đã xử lý" gọi TRƯỚC side effect, nên side effect
/// lỗi là mất message vĩnh viễn: MassTransit gửi lại, thấy dấu, bỏ qua, rồi ACK. Email/SMS/đồng
/// bộ DB không bao giờ chạy mà cũng không ai biết.
/// <para>
/// Vòng đời đúng gồm ba bước: <see cref="TryBeginAsync"/> giữ chỗ có hạn →
/// <see cref="CompleteAsync"/> khi side effect xong → <see cref="ReleaseAsync"/> khi side effect
/// lỗi. Chỗ giữ có hạn để tiến trình chết giữa chừng không khoá message lại nhiều ngày.
/// </para>
/// </remarks>
public interface IInboxStore
{
    /// <summary>Xin quyền xử lý. Xem <see cref="InboxClaimStatus"/> cho ba kết cục.</summary>
    Task<InboxClaim> TryBeginAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu ĐÃ XONG — từ giờ mới thực sự là trùng lặp.</summary>
    Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default);

    /// <summary>Nhả chỗ giữ khi side effect lỗi, để lần gửi lại được chạy thật.</summary>
    Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default);
}
