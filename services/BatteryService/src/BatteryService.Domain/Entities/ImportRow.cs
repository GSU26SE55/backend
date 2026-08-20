using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Một dòng dữ liệu trong lô import, kèm kết quả xử lý của chính dòng đó.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreatedEntityId"/> là thứ khiến việc hoàn tác khả thi mà không phải đụng vào schema
/// của <c>battery_assets</c> hay <c>sites</c>. Hai bảng đó đang mang dữ liệu thật; thêm cột vào
/// chúng là migration xâm lấn, phải nạp bù dữ liệu cũ và mang rủi ro không cần thiết. Ghi ở đây thì
/// hoàn tác chỉ là duyệt các dòng của lô.
/// </para>
/// <para>
/// <see cref="RawJson"/> giữ nguyên dòng gốc trước mọi bước chuẩn hoá. Khi đối tác hỏi "sao số của
/// tôi lại thành thế này", đây là chỗ duy nhất trả lời được.
/// </para>
/// </remarks>
public class ImportRow : AuditableEntity
{
    public Guid ImportBatchId { get; set; }

    public ImportBatch Batch { get; set; } = null!;

    /// <summary>Số dòng trong file gốc (tính cả dòng tiêu đề), để người dùng dò ngược.</summary>
    public int RowNumber { get; set; }

    public ImportEntityTypeEnum EntityType { get; set; }

    /// <summary>Dòng gốc dạng JSON, giữ nguyên chưa qua chuẩn hoá.</summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>Mã của bên thứ ba cho bản ghi này, đã chuẩn hoá.</summary>
    public string ExternalRef { get; set; } = string.Empty;

    public ImportRowStatusEnum Status { get; set; } = ImportRowStatusEnum.Pending;

    /// <summary>Danh sách lỗi dạng JSON: <c>[{"field":"Email","detail":"..."}]</c>.</summary>
    public string? ErrorsJson { get; set; }

    /// <summary>Bản ghi nghiệp vụ do dòng này tạo ra. Null khi chưa ghi hoặc chỉ liên kết.</summary>
    public Guid? CreatedEntityId { get; set; }

    /// <summary>Tài khoản mà AuthService trả về (chỉ có ý nghĩa với dòng khách hàng).</summary>
    public Guid? LinkedAccountId { get; set; }

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Số lần đã thử ghi. Dùng để dừng lại thay vì thử lại vô hạn.</summary>
    public int AttemptCount { get; set; }
}
