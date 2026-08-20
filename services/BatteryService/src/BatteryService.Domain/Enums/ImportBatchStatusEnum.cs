namespace BatteryService.Domain.Enums;

/// <summary>Vòng đời của một lô import.</summary>
/// <remarks>
/// <para>
/// Hai trạng thái <see cref="Committing"/> và <see cref="AwaitingAccountSync"/> tách riêng có chủ ý:
/// lô đang ghi dữ liệu khác hẳn lô đang <i>chờ</i> bản sao khách hàng từ AuthService. Gộp lại thì
/// người vận hành nhìn vào không biết lô đứng im là do hệ thống bận hay do event bus tắc.
/// </para>
/// </remarks>
public enum ImportBatchStatusEnum
{
    /// <summary>Vừa nhận, chưa xử lý.</summary>
    Pending = 1,

    /// <summary>Đang đọc file.</summary>
    Parsing = 2,

    /// <summary>Đang kiểm tra từng dòng.</summary>
    Validating = 3,

    /// <summary>File hỏng hoặc thiếu cột bắt buộc — không có dòng nào dùng được.</summary>
    ValidationFailed = 4,

    /// <summary>Kiểm tra xong, chờ người dùng bấm ghi thật.</summary>
    ReadyToCommit = 5,

    /// <summary>Đang ghi dữ liệu nghiệp vụ.</summary>
    Committing = 6,

    /// <summary>Đã yêu cầu cấp tài khoản, đang chờ bản sao khách hàng đồng bộ về.</summary>
    AwaitingAccountSync = 7,

    /// <summary>Ghi xong, không dòng nào hỏng.</summary>
    Completed = 8,

    /// <summary>Ghi xong nhưng có dòng hỏng.</summary>
    CompletedWithErrors = 9,

    /// <summary>Đang hoàn tác.</summary>
    Reverting = 10,

    /// <summary>Đã hoàn tác xong.</summary>
    Reverted = 11,

    /// <summary>Hỏng ở mức lô (lỗi hệ thống, hết thời gian chờ).</summary>
    Failed = 12
}
