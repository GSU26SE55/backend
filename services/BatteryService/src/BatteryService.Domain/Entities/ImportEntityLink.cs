using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Bản đồ giữa mã bản ghi trong file của bên thứ ba và định danh bên hệ thống mình.
/// </summary>
/// <remarks>
/// <para>
/// Đây là thứ khiến lần nạp thứ hai trở thành <i>cập nhật</i> thay vì đẻ ra bản sao. Không có bảng
/// này thì mỗi lần nhận lại file là một bộ khách hàng và site mới hoàn toàn, và không có cách nào
/// phát hiện ngoài việc nhìn bằng mắt.
/// </para>
/// <para>
/// <see cref="ExternalRefRaw"/> giữ mã gốc chưa chuẩn hoá vì lúc đối chiếu ngược với bên cung cấp
/// dữ liệu, thứ họ đọc được là mã trong hệ thống của họ, không phải mã đã bị mình viết hoa và thay
/// ký tự.
/// </para>
/// </remarks>
public class ImportEntityLink : AuditableEntity
{
    public ImportEntityTypeEnum EntityType { get; set; }

    /// <summary>Mã trong file nguồn, đã chuẩn hoá — dùng để tra.</summary>
    public string ExternalRef { get; set; } = string.Empty;

    /// <summary>Mã trong file nguồn, nguyên bản như đã nhận.</summary>
    public string ExternalRefRaw { get; set; } = string.Empty;

    /// <summary>Định danh bản ghi tương ứng trong hệ thống mình.</summary>
    public Guid InternalId { get; set; }

    /// <summary>Lô import đã tạo ra liên kết này.</summary>
    public Guid? CreatedByBatchId { get; set; }
}
