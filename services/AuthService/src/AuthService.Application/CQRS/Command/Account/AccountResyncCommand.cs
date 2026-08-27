using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// 02/08/2026 — Phát lại <c>AccountSyncSnapshotEvent</c> cho account để các service khác dựng lại
/// read-model của mình.
///
/// <para><b>Vì sao cần một lệnh đối soát riêng:</b> read-model chỉ được nuôi bằng event, mà event
/// chỉ phát khi có thao tác. Account tạo bằng <c>AuthDataSeeder</c> (ghi thẳng DbContext) chưa bao
/// giờ phát event nào, nên chúng không tồn tại trong read-model của NotificationService —
/// <c>GetActiveByRoleAsync("Admin")</c> vì thế trả rỗng và mọi thông báo gửi cho nhóm Admin đều rơi
/// vào nhánh "không có người nhận → skip". Không có cách nào tự sửa được từ bên trong
/// NotificationService vì mỗi service một database.</para>
///
/// <para>Lệnh này an toàn khi chạy lại nhiều lần: snapshot là upsert thuần ở phía consumer, không
/// kèm tác dụng phụ nghiệp vụ nào (không gửi welcome, không ghi notification). Production gọi
/// lệnh tự động theo chu kỳ qua account-projection reconciliation worker; endpoint admin vẫn hữu
/// ích khi cần đối soát ngay một account hoặc toàn bộ dữ liệu.</para>
/// </summary>
public class AccountResyncCommand : IRequest<AccountResyncResponse>
{
    /// <summary>
    /// Null = đối soát toàn bộ account. Có giá trị = chỉ phát lại cho đúng một account.
    /// </summary>
    public Guid? AccountId { get; set; }
}
