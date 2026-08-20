using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Một lần nạp dữ liệu khách hàng và thiết bị từ bên thứ ba.
/// </summary>
/// <remarks>
/// <para>
/// Lô tồn tại vì việc nạp không thể gói trong một request: tài khoản khách hàng do AuthService cấp
/// qua message bus, nên giữa lúc yêu cầu và lúc bản sao khách hàng về tới nơi có một khoảng chờ.
/// Không có bảng trạng thái thì khoảng chờ đó không quan sát được và không thử lại được.
/// </para>
/// <para>
/// Lô cũng là đơn vị hoàn tác. Người dùng nạp nhầm file thì gỡ theo lô, không phải đi dò từng bản ghi.
/// </para>
/// </remarks>
public class ImportBatch : AuditableEntity
{
    /// <summary>Tên file gốc, để người dùng nhận ra lô của mình.</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// SHA-256 của nội dung file (hex thường). Dùng để chặn nạp lại đúng file đã nạp.
    /// </summary>
    /// <remarks>
    /// Chặn theo nội dung chứ không theo tên file: đối tác hay gửi lại cùng một file với tên khác
    /// ("khach-hang.csv", "khach-hang-final.csv"), và đó chính là ca dễ nhân đôi dữ liệu nhất.
    /// </remarks>
    public string FileSha256 { get; set; } = string.Empty;

    public ImportBatchStatusEnum Status { get; set; } = ImportBatchStatusEnum.Pending;

    /// <summary>True = chỉ kiểm tra, không ghi bất kỳ dữ liệu nghiệp vụ nào.</summary>
    public bool IsDryRun { get; set; } = true;

    /// <summary>Tài khoản Admin đã bấm nạp.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>Bitmask <see cref="ImportEntityTypeEnum"/> — các loại dữ liệu có mặt trong lô.</summary>
    public int EntityTypesMask { get; set; }

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int InvalidRows { get; set; }

    public int CreatedRows { get; set; }

    public int UpdatedRows { get; set; }

    public int SkippedRows { get; set; }

    public int FailedRows { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Lỗi ở mức lô: file hỏng, thiếu cột bắt buộc, vượt số dòng cho phép.</summary>
    public string? ErrorSummary { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
