using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// BatteryService yêu cầu AuthService cấp tài khoản cho một khách hàng đến từ dữ liệu import
/// của bên thứ ba (xem <c>PARTNER_DATA_IMPORT_PLAN.md</c> — pha 2A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phải đi qua event thay vì gọi thẳng AuthService:</b> hai service có database riêng và
/// dự án không có kênh gọi đồng bộ nào giữa chúng (<c>Protos/auth.proto</c> vẫn rỗng). Event bus là
/// đường liên lạc duy nhất đang chạy ổn định.
/// </para>
/// <para>
/// AuthService xử lý event này phải <b>idempotent theo <see cref="Email"/></b>: email đã tồn tại thì
/// coi là <i>liên kết</i> vào tài khoản cũ, KHÔNG phải lỗi. Đối tác nào bàn giao dữ liệu cũng có sẵn
/// một phần khách đã là khách của mình.
/// </para>
/// <para>
/// Tài khoản được tạo ở <c>Status = Active</c> chứ không phải chế độ mời. Lý do bắt buộc: handler tạo
/// Site/BatteryAsset của BatteryService đòi bản sao khách hàng phải <c>IsActive</c>, mà bản sao đó chỉ
/// sinh ra từ <c>AccountActivatedEvent</c> — đường mời không phát event này.
/// </para>
/// </remarks>
/// <param name="BatchId">Lô import đang chạy — dùng để đối chiếu ngược khi có sự cố.</param>
/// <param name="RowId">Dòng cụ thể trong lô, để cập nhật đúng trạng thái khi có kết quả.</param>
/// <param name="ExternalCustomerCode">Mã khách hàng theo hệ thống của bên thứ ba (đã chuẩn hoá).</param>
/// <param name="Email">Email khách hàng — khoá tự nhiên để chống tạo trùng.</param>
/// <param name="FullName">Họ tên khách hàng.</param>
/// <param name="PhoneNumber">Số điện thoại; có thể null hoặc bị bỏ trống nếu trùng tài khoản khác.</param>
public record PartnerCustomerProvisionRequestedEvent(
    Guid BatchId,
    Guid RowId,
    string ExternalCustomerCode,
    string Email,
    string FullName,
    string? PhoneNumber
) : IntegrationEvent;
