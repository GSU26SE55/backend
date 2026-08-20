using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// AuthService trả kết quả cấp tài khoản về cho BatteryService, khép lại pha 2A của luồng import
/// dữ liệu bên thứ ba.
/// </summary>
/// <remarks>
/// <para>
/// Event này mang <see cref="AccountId"/> — thứ mà BatteryService không tự sinh ra được vì tài khoản
/// thuộc quyền sở hữu của AuthService. Nhận được nó, BatteryService mới ghi được bản đồ
/// <c>ExternalCustomerCode → AccountId</c> và mở khoá cho pha 2B (tạo Site, BatteryAsset, IotDevice).
/// </para>
/// <para>
/// <see cref="FailureReason"/> khác null nghĩa là không cấp được tài khoản (ví dụ thiếu vai trò
/// Customer trong hệ thống). Khi đó <see cref="AccountId"/> là <see cref="Guid.Empty"/> và
/// BatteryService phải đánh dòng đó hỏng kèm lý do, thay vì chờ mãi một bản sao không bao giờ tới.
/// </para>
/// </remarks>
/// <param name="BatchId">Lô import tương ứng.</param>
/// <param name="RowId">Dòng import tương ứng.</param>
/// <param name="ExternalCustomerCode">Mã khách hàng theo hệ thống của bên thứ ba.</param>
/// <param name="AccountId">Tài khoản vừa tạo hoặc tài khoản cũ được liên kết; rỗng khi thất bại.</param>
/// <param name="WasExisting">True khi liên kết vào tài khoản đã có sẵn thay vì tạo mới.</param>
/// <param name="FailureReason">Lý do thất bại; null khi thành công.</param>
public record PartnerCustomerProvisionedEvent(
    Guid BatchId,
    Guid RowId,
    string ExternalCustomerCode,
    Guid AccountId,
    bool WasExisting,
    string? FailureReason
) : IntegrationEvent;
